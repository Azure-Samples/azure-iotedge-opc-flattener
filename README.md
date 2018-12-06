# Azure IoT Edge Protocol translation sample

Simple sample showing how to use to do a protocol translation in an [Azure IoT Edge](https://azure.microsoft.com/en-us/services/iot-edge/) module. The output of the [OPC-UA Publisher](https://github.com/Azure/iot-edge-opc-publisher) module will be flattened, transformed and enriched by the OPCFlattenerModule.

The OPC-UA Publisher first of all is an [OPC-UA](https://opcfoundation.org/) client application, that gets notifications of changes from monitored OPC-UA Servers. These changes of OPC-UA Nodes are gathered till a certain amount of time has passed by or the maximum message size for the resulting message is reached. Both parameter, IoT Hub send interval and IoT Hub message size, can be configured:

       --ms, --iothubmessagesize=VALUE
                              the max size of a message which can be send to
                                IoTHub. when telemetry of this size is available
                                it will be sent.
                                0 will enforce immediate send when telemetry is
                                available
                                Min: 0
                                Max: 262144
                                Default: 262144
       --si, --iothubsendinterval=VALUE
                              the interval in seconds when telemetry should be
                                send to IoTHub. If 0, then only the
                                iothubmessagesize parameter controls when
                                telemetry is sent.
                                Default: '10'

For more details on this configuration options see the [OPC-UA Publisher documentation on performance and memory considerations](https://github.com/Azure/iot-edge-opc-publisher#performance-and-memory-considerations).

All changes of monitored OPC-UA Server nodes are put into a message as a Json array, looking like the following sample from the [OPC-UA Server simulator](https://github.com/Azure-Samples/iot-edge-opc-plc) (and used in this sample):

```json
[
  {
    "NodeId": "nsu=http://microsoft.com/Opc/OpcPlc/;s=SpikeData",
    "ApplicationUri": "urn:OpcPlc:opcplc",
    "DisplayName": "ns=2;s=SpikeData",
    "Value": {
      "Value": 36.812455268467772,
      "SourceTimestamp": "2018-11-27T21:21:49.1997653Z"
    }
  },
  {
    "NodeId": "nsu=http://microsoft.com/Opc/OpcPlc/;s=DipData",
    "ApplicationUri": "urn:OpcPlc:opcplc",
    "DisplayName": "ns=2;s=DipData",
    "Value": {
      "Value": 36.812455268467772,
      "SourceTimestamp": "2018-11-27T21:21:49.1997409Z"
    }
  },
  {
    "NodeId": "nsu=http://microsoft.com/Opc/OpcPlc/;s=AlternatingBoolean",
    "ApplicationUri": "urn:OpcPlc:opcplc",
    "DisplayName": "ns=2;s=AlternatingBoolean",
    "Value": {
      "Value": false,
      "SourceTimestamp": "2018-11-27T21:21:49.1290318Z"
    }
  },
  {
    "NodeId": "nsu=http://microsoft.com/Opc/OpcPlc/;s=RandomUnsignedInt32",
    "ApplicationUri": "urn:OpcPlc:opcplc",
    "DisplayName": "ns=2;s=RandomUnsignedInt32",
    "Value": {
      "Value": 1676825315,
      "SourceTimestamp": "2018-11-27T21:21:49.2000288Z"
    }
  },
  {
    "NodeId": "nsu=http://microsoft.com/Opc/OpcPlc/;s=PositiveTrendData",
    "ApplicationUri": "urn:OpcPlc:opcplc",
    "DisplayName": "ns=2;s=PositiveTrendData",
    "Value": {
      "Value": 100,
      "SourceTimestamp": "2018-11-27T21:21:49.1471567Z"
    }
  },
  {
    "NodeId": "nsu=http://microsoft.com/Opc/OpcPlc/;s=NegativeTrendData",
    "ApplicationUri": "urn:OpcPlc:opcplc",
    "DisplayName": "ns=2;s=NegativeTrendData",
    "Value": {
      "Value": 100,
      "SourceTimestamp": "2018-11-27T21:21:49.1471527Z"
    }
  },
  {
    "NodeId": "nsu=http://opcfoundation.org/UA/;i=2258",
    "ApplicationUri": "urn:OpcPlc:opcplc",
    "DisplayName": "i=2258",
    "Value": {
      "Value": "2018-11-27T21:21:50.0389295Z",
      "SourceTimestamp": "2018-11-27T21:21:50.0389295Z"
    }
  },
  {
    "NodeId": "nsu=http://microsoft.com/Opc/OpcPlc/;s=SpikeData",
    "ApplicationUri": "urn:OpcPlc:opcplc",
    "DisplayName": "ns=2;s=SpikeData",
    "Value": {
      "Value": 99.80267284282715,
      "SourceTimestamp": "2018-11-27T21:21:50.0972836Z"
    }
  },
  {
    "NodeId": "nsu=http://microsoft.com/Opc/OpcPlc/;s=DipData",
    "ApplicationUri": "urn:OpcPlc:opcplc",
    "DisplayName": "ns=2;s=DipData",
    "Value": {
      "Value": 99.80267284282715,
      "SourceTimestamp": "2018-11-27T21:21:50.0972755Z"
    }
  },
  {
    "NodeId": "nsu=http://microsoft.com/Opc/OpcPlc/;s=RandomUnsignedInt32",
    "ApplicationUri": "urn:OpcPlc:opcplc",
    "DisplayName": "ns=2;s=RandomUnsignedInt32",
    "Value": {
      "Value": 264488574,
      "SourceTimestamp": "2018-11-27T21:21:50.1984841Z"
    }
  }
]
```
Unfortunately this Json array cannot be used for Stream based processing, because very often complete data frames with a value for each data point in time stamped manner is expected. It's even possible that this array contains multiple entries for the same OPC-UA Node with different time stamps. A lot of event processing tools and systems are expecting a much easier, flattened data format as input. Based on the data from above something like:

```json
{
  "messstelle": "Buegel hinten links",
  "urn:OpcPlc:opcplc;ns=2;s=SpikeData": 99.80267284282715,
  "DipData": 99.80267284282715,
  "urn:OpcPlc:opcplc;ns=2;s=AlternatingBoolean": false,
  "urn:OpcPlc:opcplc;ns=2;s=RandomUnsignedInt32": 264488574,
  "urn:OpcPlc:opcplc;ns=2;s=PositiveTrendData": 100,
  "urn:OpcPlc:opcplc;ns=2;s=NegativeTrendData": 100,
  "Current_time": "2018-11-27T21:21:50.0389295Z",
  "TimeCreated": "2018-11-27T21:21:50.1984841Z"
}
```
The json message is now completly flat, an extra static property 'messstelle' was added, property names were replaced (DipData and Current_time), and a time stamp property 'TimeCreated' was added that contains the latest used time from all events in the original message. For properties like 'Current_time' (DisplayName "i=2258" in the original message) that have multiple entries, the latest one with its value was added to the message.

## Configuration

This sample contains a fully working setup for Azure IoT Edge, including a message producing OPC-UA PLC simulator, a configured OPC-UA Publisher module and the Edge module that shows the flattening of the messages as described above. The OPC-UA PLC simulator uses its standard configuration. The Publisher uses the [publisednodes.json](./appdata/publishednodes.json) and [telemetryconfig.json](./appdata/telemetryconfig.json) files for its configuration. Make sure that you use the correct Docker Binds in your [deployment manifest file](./deployment.template.json) for Azure IoT Edge.

### Routing for Azure IoT Edge

See an example for the routing below. It's very important to use ```/messages/modules/publisher``` as the source for messages sent by the OPC-UA Publisher. The Flattener uses ```input1``` and ```output1``` as input and output target in the data processing pipeline.

```json
"routes": {
          "OPCFlattenerModuleToIoTHub": "FROM /messages/modules/flattener/outputs/* INTO $upstream",
          "sensorToOPCFlattenerModule": "FROM /messages/modules/publisher INTO BrokeredEndpoint(\"/modules/flattener/inputs/input1\")"
        },
```

### Using templates for the resulting message

If you want to use a template with some static contents (e.g. the 'messstelle' property in the example above), than you can speficy a json file that contains that template (see the file [template.json](./appdata/template.json) as an example). You can specify the template file by using the command line argument ```--template``` followed by the file path to the json file.

### Change of display names / Json property names

Display names, shown as Json property names in the resulting message, can be changed from standard to a configured string for each monitored node. Normally the Json property is build up by the Application Uri and the Node Id, seperated by a semicolon. E.g. ```urn:OpcPlc:opcplc;ns=2;s=NegativeTrendData```. By specifying a mapping configuration file using the command line argument ```--mapping``` followed by the file path to the json file, customized display names can be used. E.g. as used in the above example:

```json
{
  "NodesMapping": [
    {
      "nsu=http://opcfoundation.org/UA/;i=2258": {
        "DisplayName": "Current_time"
      }
    },
    {
      "nsu=http://microsoft.com/Opc/OpcPlc/;s=DipData": {
        "DisplayName": "DipData"
      }
    }
  ]
}
```

## Planned extensions

Currently the latest entry for nodes that have multiple entries in the message from the OPC-UA Publisher is used in the new message. A future extension will provide different forms of aggregation, e.g. average, count, min, max, for the values of these nodes.


