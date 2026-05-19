using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VirtualTabGroups.Core
{
    /// <summary>
    /// Custom Newtonsoft JsonConverter for the polymorphic TreeNodeModel hierarchy.
    /// Uses a "type" discriminator field ("file" | "folder") to branch on both
    /// read and write paths, avoiding the need for $type metadata.
    /// </summary>
    public sealed class NodeJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            typeof(TreeNodeModel).IsAssignableFrom(objectType);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            switch (value)
            {
                case FileNode file:
                    writer.WriteStartObject();
                    writer.WritePropertyName("type"); writer.WriteValue("file");
                    writer.WritePropertyName("id");   writer.WriteValue(file.Id.ToString());
                    writer.WritePropertyName("name"); writer.WriteValue(file.Name);
                    writer.WritePropertyName("path"); writer.WriteValue(file.Path);
                    writer.WriteEndObject();
                    return;

                case FolderNode folder:
                    writer.WriteStartObject();
                    writer.WritePropertyName("type");     writer.WriteValue("folder");
                    writer.WritePropertyName("id");       writer.WriteValue(folder.Id.ToString());
                    writer.WritePropertyName("name");     writer.WriteValue(folder.Name);
                    writer.WritePropertyName("expanded"); writer.WriteValue(folder.Expanded);
                    writer.WritePropertyName("children");
                    writer.WriteStartArray();
                    foreach (var child in folder.Children)
                    {
                        WriteJson(writer, child, serializer);
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    return;

                default:
                    throw new JsonSerializationException("Unknown node type: " + value?.GetType());
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            var type = (string)obj["type"];
            switch (type)
            {
                case "file":
                    var file = new FileNode((string)obj["name"], (string)obj["path"]);
                    file.Id = Guid.Parse((string)obj["id"]);
                    return file;

                case "folder":
                    var folder = new FolderNode((string)obj["name"]);
                    folder.Id = Guid.Parse((string)obj["id"]);
                    folder.Expanded = (bool?)obj["expanded"] ?? false;
                    var children = (JArray)obj["children"] ?? new JArray();
                    foreach (var child in children)
                    {
                        var childNode = (TreeNodeModel)ReadJson(child.CreateReader(), typeof(TreeNodeModel), null, serializer);
                        folder.Children.Add(childNode);
                    }
                    return folder;

                default:
                    throw new JsonSerializationException("Unknown node type discriminator: " + type);
            }
        }
    }
}
