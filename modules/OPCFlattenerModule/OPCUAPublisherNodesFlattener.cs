using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OPCUAPubFlattener
{
    public class OPCUAPublisherNodesFlattener
    {
        public const string DEFAULT_NODEIDPROPERTNAME = "NodeId";

        public string NodeIdPropertyname { get; set; } = DEFAULT_NODEIDPROPERTNAME;

        public const string DEFAULT_DISPLAYNAMEPROPERTYNAME = "DisplayName";
        public string DisplayNamePropertyname { get; set; } = DEFAULT_DISPLAYNAMEPROPERTYNAME;

        public const string DEFAULT_TIMECREATEDPROPERTYNAME = "TimeCreated";
        public string TimeCreatedPropertyname { get; set; } = DEFAULT_TIMECREATEDPROPERTYNAME;

        public const string VALUEPROPERTYNAME = "Value";
        public bool DoAddTimeCreatedProperty { get; set; } = true;

        public bool Verbose { get; set; } = false;

        public JObject OutputTemplate { get; set; }

        public Dictionary<string, string> DisplayNames { get; set; }

        private int counter;

        public Message DoFlattenMessage(Message message)
        {
            int counterValue = Interlocked.Increment(ref counter);

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);

            if(this.Verbose)
                Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            var pipeMessage = new Message(Encoding.UTF8.GetBytes(this.DoFlatten(messageString)));
            foreach (var prop in message.Properties)
            {
                pipeMessage.Properties.Add(prop.Key, prop.Value);
            }

            return pipeMessage;
        }

        public string DoFlattenFromFile(string jsonfile)
        {
            return this.DoFlatten(File.ReadAllText(jsonfile));
        }

        public string DoFlatten(string jsonmessage)
        {
            JObject result = this.OutputTemplate != null ? (JObject)this.OutputTemplate.DeepClone() : new JObject();

            JArray opcNodes = JArray.Parse(jsonmessage);
            DateTime latest = DateTime.MinValue;

            foreach (var anode in opcNodes)
            {
                string displayname = anode[this.DisplayNamePropertyname] != null ? anode[this.DisplayNamePropertyname].ToString() : null;
                if ((this.DisplayNames != null && this.DisplayNames.ContainsKey(anode[this.NodeIdPropertyname].ToString())) 
                    || (displayname == null || displayname.Length == 0))
                {
                    displayname = this.DisplayNames[anode[this.NodeIdPropertyname].ToString()];
                }

                result.Add(new JProperty(displayname, anode[VALUEPROPERTYNAME][VALUEPROPERTYNAME]));
                DateTime aDate = DateTime.Parse((anode[VALUEPROPERTYNAME]["SourceTimestamp"]).ToString());
                if (aDate > latest)
                    latest = aDate;

            }

            if (this.DoAddTimeCreatedProperty)
                result.Add(new JProperty(this.TimeCreatedPropertyname, latest));

            string resultString = result.ToString();

            if (this.Verbose)
                Console.WriteLine($"Flattened message: Body: [{resultString}]");

            return resultString;
        }

        public void UseOutputTemplateFromFile(string jsonfile)
        {
            this.OutputTemplate = JObject.Parse(File.ReadAllText(jsonfile));
        }

        public void UseMappingConfigurationFromFile(string jsonfile)
        {
            this.DisplayNames = new Dictionary<string, string>();

            JObject config = JObject.Parse(File.ReadAllText(jsonfile));
            JArray mappings = (JArray)config.GetValue("NodesMapping");
            foreach(var nodeMapping in mappings)
            {
                string nodeid = ((JProperty)nodeMapping.First).Name;
                this.DisplayNames.Add(nodeid, nodeMapping[nodeid]["DisplayName"].ToString());
            }
        }

        public async Task<string> DoFlattenAsync(string jsonmessage)
        {
            JsonTextReader reader = new JsonTextReader(new StringReader(jsonmessage));

            StringBuilder _jsonStringBuilder = new StringBuilder();
            StringWriter _jsonStringWriter = new StringWriter(_jsonStringBuilder);
            using (JsonWriter _jsonWriter = new JsonTextWriter(_jsonStringWriter))
            {
                await _jsonWriter.WriteStartObjectAsync();

                while (reader.Read())
                {
                    bool foundDisplayname = await ReadTillNextProp(reader, "DisplayName");

                    if (foundDisplayname)
                    {
                        reader.Read();
                        string propName = reader.Value.ToString();

                        if (await ReadTillNextProp(reader, "Value") && await ReadTillNextProp(reader, "Value") && reader.Read())
                        {
                            await _jsonWriter.WritePropertyNameAsync(propName);
                            await _jsonWriter.WriteValueAsync(reader.Value);
                        }
                    }
                }

                await _jsonWriter.WriteEndObjectAsync();
                await _jsonWriter.FlushAsync();
            }

            return _jsonStringBuilder.ToString();
        }

        private async Task<bool> ReadTillNextProp(JsonTextReader reader, string propName)
        {
            while (await reader.ReadAsync())
            {
                if (reader.Value != null && reader.Value.Equals(propName))
                    return true;
            }

            return false;
        }
    }
}
