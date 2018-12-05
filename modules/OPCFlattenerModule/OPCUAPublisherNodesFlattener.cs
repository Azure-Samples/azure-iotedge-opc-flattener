using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
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

        public bool DoUseApplicationUri { get; set; } = true;

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
            string resultString = "{}";

            if (jsonmessage != null && jsonmessage.Length > 2)
            {
                if (jsonmessage.StartsWith('['))
                    resultString = this.DoStandardFlattening(JArray.Parse(jsonmessage));
                else if (jsonmessage.StartsWith('{'))
                {
                    JArray opcNodes = new JArray();
                    opcNodes.Add(JObject.Parse(jsonmessage));
                    resultString = this.DoStandardFlattening(opcNodes);
                }
            }

            return resultString;
        }

        protected string DoStandardFlattening(JArray opcNodes)
        {
            Dictionary<ApplicationNodeId, List<KeyValuePair<DateTime, JObject>>> nodes = this.CreateNodeTimeseries(opcNodes);

            JObject result = this.OutputTemplate != null ? (JObject)this.OutputTemplate.DeepClone() : new JObject();
            DateTime latest = DateTime.MinValue;

            foreach (var nodeEntry in nodes)
            {
                List<KeyValuePair<DateTime, JObject>> nodeTSEntries = nodeEntry.Value;

                if (nodeTSEntries.Count > 0)
                {
                    JObject anode = nodeTSEntries[nodeTSEntries.Count - 1].Value;

                    string displayname = (this.DoUseApplicationUri ? anode["ApplicationUri"].ToString() : "") + ";" +
                        (anode[this.DisplayNamePropertyname] != null ? anode[this.DisplayNamePropertyname].ToString() : null);
                    if ((this.DisplayNames != null && this.DisplayNames.ContainsKey(anode[this.NodeIdPropertyname].ToString()))
                        || (displayname == null || displayname.Length == 0))
                    {
                        displayname = this.DisplayNames[anode[this.NodeIdPropertyname].ToString()];
                    }

                    result.Add(new JProperty(displayname, anode[VALUEPROPERTYNAME][VALUEPROPERTYNAME]));

                    DateTime aDate = nodeTSEntries[nodeTSEntries.Count - 1].Key;
                    if (aDate > latest)
                        latest = aDate;
                }
            }

            if (this.DoAddTimeCreatedProperty)
                result.Add(new JProperty(this.TimeCreatedPropertyname, latest));

            string resultString = result.ToString();

            if (this.Verbose)
                Console.WriteLine($"Flattened message: Body: [{resultString}]");

            return resultString;
        }

        private Dictionary<ApplicationNodeId, List<KeyValuePair<DateTime, JObject>>> CreateNodeTimeseries(JArray opcNodes)
        {
            Dictionary<ApplicationNodeId, List<KeyValuePair<DateTime, JObject>>> result = new Dictionary<ApplicationNodeId, List<KeyValuePair<DateTime, JObject>>>(new ApplicationNodeIdComparer());

            foreach (var anode in opcNodes)
            {
                ApplicationNodeId applicationNodeId = new ApplicationNodeId(anode["ApplicationUri"].ToString(), anode[this.NodeIdPropertyname].ToString());
                DateTime aDate = anode[VALUEPROPERTYNAME]["SourceTimestamp"].Value<DateTime>();
                if (result.ContainsKey(applicationNodeId))
                {
                    List<KeyValuePair<DateTime, JObject>> entry = result[applicationNodeId];
                    entry.Add(new KeyValuePair<DateTime, JObject>(aDate, (JObject)anode));
                    entry.Sort(delegate(KeyValuePair<DateTime, JObject> first, KeyValuePair<DateTime, JObject> second) {
                        if (first.Key == null && second.Key == null) return 0;
                        else if (first.Key == null) return -1;
                        else if (second.Key == null) return 1;
                        else return first.Key.CompareTo(second.Key);
                    });
                } else
                {
                    result.Add(applicationNodeId, new List<KeyValuePair<DateTime, JObject>> () { new KeyValuePair<DateTime, JObject>(aDate, (JObject)anode) });
                }
            }

            return result;
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

    public class ApplicationNodeId
    {
        public string ApplicationUri { get; set; }

        public string NodeId { get; set; }

        public ApplicationNodeId(string appUri, string nodeId)
        {
            this.ApplicationUri = appUri;
            this.NodeId = nodeId;
        }
    }

    public class ApplicationNodeIdComparer : IEqualityComparer<ApplicationNodeId>
    {
        #region IEqualityComparer<ApplicationNodeId> Members

        public bool Equals(ApplicationNodeId x, ApplicationNodeId y)
        {
            return ((x.ApplicationUri == y.ApplicationUri) & (x.NodeId == y.NodeId));
        }

        public int GetHashCode(ApplicationNodeId obj)
        {
            string combined = obj.ApplicationUri + "|" + obj.NodeId;
            return (combined.GetHashCode());
        }

        #endregion
    }
}
