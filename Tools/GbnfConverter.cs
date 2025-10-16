using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using System.ComponentModel.DataAnnotations;

namespace LetheAISharp
{

    public static class GbnfConverter
    {
        private static readonly HashSet<string> _definedRules = [];
        private static readonly StringBuilder _output = new();

        public static string Convert(string jsonSchema, string rootRuleName = "root")
        {
            _definedRules.Clear();
            _output.Clear();

            var schema = JsonNode.Parse(jsonSchema) ?? throw new ArgumentException("Invalid JSON schema");
            GenerateRule(rootRuleName, schema);

            // Add common utility rules at the end
            AppendUtilityRules();

            return _output.ToString();
        }

        public static string Convert(Type type, string rootRuleName = "root")
        {
            var jsonSchema = ConvertToSchema(type);
            return Convert(jsonSchema, rootRuleName);
        }

        private static string ConvertToSchema(Type type)
        {
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            };

            var required = new JsonArray();
            var properties = schema["properties"]!.AsObject();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propSchema = GeneratePropertySchema(prop);
                properties[prop.Name] = propSchema;

                // Check if property is required
                if (prop.GetCustomAttribute<RequiredAttribute>() != null)
                {
                    required.Add(prop.Name);
                }
            }

            if (required.Count > 0)
            {
                schema["required"] = required;
            }

            return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }


        private static void GenerateRule(string ruleName, JsonNode? schema)
        {
            if (_definedRules.Contains(ruleName))
                return;

            _definedRules.Add(ruleName);

            var type = schema?["type"]?.GetValue<string>();

            if (type == "object")
            {
                GenerateObjectRule(ruleName, schema);
            }
            else if (type == "array")
            {
                GenerateArrayRule(ruleName, schema);
            }
            else if (type == "string")
            {
                GenerateStringRule(ruleName, schema);
            }
            else if (type == "number" || type == "integer")
            {
                GenerateNumberRule(ruleName, type == "integer");
            }
            else if (type == "boolean")
            {
                _output.AppendLine($"{ruleName} ::= \"true\" | \"false\"");
            }
            else if (type == "null")
            {
                _output.AppendLine($"{ruleName} ::= \"null\"");
            }
            else
            {
                // Fallback for unknown types
                _output.AppendLine($"{ruleName} ::= string");
            }
        }

        private static void GenerateObjectRule(string ruleName, JsonNode? schema)
        {
            var properties = schema?["properties"]?.AsObject();
            var required = schema?["required"]?.AsArray()
                .Select(n => n!.GetValue<string>())
                .ToHashSet() ?? [];

            if (properties == null || properties.Count == 0)
            {
                _output.AppendLine($"{ruleName} ::= \"{{\" ws \"}}\"");
                return;
            }

            // Build the main object rule
            _output.Append($"{ruleName} ::= \"{{\" ws ");

            var props = properties.ToList();
            for (int i = 0; i < props.Count; i++)
            {
                var propName = props[i].Key;
                var propRuleName = $"{ruleName}-{SanitizeRuleName(propName)}";

                // Add quoted property name, colon, and reference to property rule
                _output.Append($"\"\\\"\" \"{EscapeString(propName)}\" \"\\\"\" ws \":\" ws {propRuleName}");

                // Add comma if not last property
                if (i < props.Count - 1)
                {
                    _output.Append(" ws \",\" ws ");
                }
            }

            _output.AppendLine(" ws \"}\"");

            // Generate the nested rules for each property's value separately
            foreach (var prop in props)
            {
                var propName = prop.Key;
                var propSchema = prop.Value;
                var propRuleName = $"{ruleName}-{SanitizeRuleName(propName)}";

                GenerateRule(propRuleName, propSchema);
            }
        }

        private static void GenerateArrayRule(string ruleName, JsonNode? schema)
        {
            var items = schema?["items"];
            var minItems = schema?["minItems"]?.GetValue<int>();
            var maxItems = schema?["maxItems"]?.GetValue<int>();

            if (items == null)
            {
                _output.AppendLine($"{ruleName} ::= \"[\" ws \"]\"");
                return;
            }

            var itemRuleName = $"{ruleName}-item";
            GenerateRule(itemRuleName, items);

            // Build the array rule with min/max constraints
            if (minItems.HasValue || maxItems.HasValue)
            {
                int min = minItems ?? 0;
                int max = maxItems ?? int.MaxValue;

                if (min == 0 && max == int.MaxValue)
                {
                    // No constraints - standard rule
                    _output.AppendLine($"{ruleName} ::= \"[\" ws ({itemRuleName} (ws \",\" ws {itemRuleName})*)? ws \"]\"");
                }
                else if (min == max)
                {
                    // Exact count
                    _output.Append($"{ruleName} ::= \"[\" ws ");
                    for (int i = 0; i < min; i++)
                    {
                        if (i > 0) _output.Append(" ws \",\" ws ");
                        _output.Append(itemRuleName);
                    }
                    _output.AppendLine(" ws \"]\"");
                }
                else if (max == int.MaxValue)
                {
                    // Only minimum
                    _output.Append($"{ruleName} ::= \"[\" ws ");
                    if (min == 0)
                    {
                        _output.AppendLine($"({itemRuleName} (ws \",\" ws {itemRuleName})*)? ws \"]\"");
                    }
                    else
                    {
                        for (int i = 0; i < min; i++)
                        {
                            if (i > 0) _output.Append(" ws \",\" ws ");
                            _output.Append(itemRuleName);
                        }
                        _output.AppendLine($" (ws \",\" ws {itemRuleName})* ws \"]\"");
                    }
                }
                else
                {
                    // Min and max - generate options for each count
                    var options = new List<string>();
                    int cappedMax = Math.Min(max, 20); // Cap at 20 to avoid grammar explosion

                    for (int count = min; count <= cappedMax; count++)
                    {
                        if (count == 0)
                        {
                            options.Add("");
                        }
                        else
                        {
                            var parts = new List<string>();
                            for (int i = 0; i < count; i++)
                            {
                                parts.Add(itemRuleName);
                            }
                            options.Add(string.Join(" ws \",\" ws ", parts));
                        }
                    }

                    if (options.Count == 1 && string.IsNullOrEmpty(options[0]))
                    {
                        _output.AppendLine($"{ruleName} ::= \"[\" ws \"]\"");
                    }
                    else
                    {
                        _output.AppendLine($"{ruleName} ::= \"[\" ws ({string.Join(" | ", options.Where(o => !string.IsNullOrEmpty(o)))}) ws \"]\"");
                    }
                }
            }
            else
            {
                // No constraints - standard rule
                _output.AppendLine($"{ruleName} ::= \"[\" ws ({itemRuleName} (ws \",\" ws {itemRuleName})*)? ws \"]\"");
            }
        }

        private static void GenerateStringRule(string ruleName, JsonNode? schema)
        {
            var enumValues = schema?["enum"]?.AsArray();
            var minLength = schema?["minLength"]?.GetValue<int>();
            var maxLength = schema?["maxLength"]?.GetValue<int>();

            if (enumValues != null && enumValues.Count != 0)
            {
                // Enum type - specific allowed values
                var options = enumValues
                    .Select(v => $"\"{EscapeString(v!.GetValue<string>())}\"")
                    .ToList();

                _output.AppendLine($"{ruleName} ::= {string.Join(" | ", options)}");
            }
            else if (minLength.HasValue || maxLength.HasValue)
            {
                // String with length constraints
                int min = minLength ?? 0;
                int max = maxLength ?? int.MaxValue;

                string charPattern = "([^\"\\\\] | \"\\\\\" [\"\\\\/bfnrt] | \"\\\\u\" [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F])";

                if (max == int.MaxValue)
                {
                    // Only minimum length
                    if (min == 0)
                    {
                        _output.AppendLine($"{ruleName} ::= string");
                    }
                    else
                    {
                        string minChars = string.Join(" ", Enumerable.Repeat(charPattern, min));
                        _output.AppendLine($"{ruleName} ::= \"\\\"\" {minChars} {charPattern}* \"\\\"\"");
                    }
                }
                else
                {
                    // Both min and max (simplified approach)
                    if (min == 0)
                    {
                        // 0 to max - just use standard string (hard to enforce max in GBNF without explosion)
                        _output.AppendLine($"{ruleName} ::= string");
                    }
                    else
                    {
                        // At least enforce minimum
                        string minChars = string.Join(" ", Enumerable.Repeat(charPattern, min));
                        _output.AppendLine($"{ruleName} ::= \"\\\"\" {minChars} {charPattern}* \"\\\"\"");
                    }
                }
            }
            else
            {
                // Regular string
                _output.AppendLine($"{ruleName} ::= string");
            }
        }

        private static void GenerateNumberRule(string ruleName, bool isInteger)
        {
            if (isInteger)
            {
                _output.AppendLine($"{ruleName} ::= integer");
            }
            else
            {
                _output.AppendLine($"{ruleName} ::= number");
            }
        }

        private static void AppendUtilityRules()
        {
            if (!_definedRules.Contains("ws"))
            {
                _output.AppendLine();
                _output.AppendLine("# Utility rules");
                _output.AppendLine("ws ::= [ \\t\\n]*");
                _output.AppendLine("string ::= \"\\\"\" ([^\"\\\\] | \"\\\\\" [\"\\\\/bfnrt] | \"\\\\u\" [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F])* \"\\\"\"");
                _output.AppendLine("number ::= \"-\"? (\"0\" | [1-9] [0-9]*) (\".\" [0-9]+)? ([eE] [-+]? [0-9]+)?");
                _output.AppendLine("integer ::= \"-\"? (\"0\" | [1-9] [0-9]*)");
            }
        }

        private static string SanitizeRuleName(string name)
        {
            // Replace invalid characters with underscores
            return new string([.. name.Select(c => char.IsLetterOrDigit(c) ? c : '_')]);
        }

        private static string EscapeString(string str)
        {
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static JsonObject GeneratePropertySchema(PropertyInfo prop)
        {
            var schema = new JsonObject();
            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            // Handle enums
            if (propType.IsEnum)
            {
                schema["type"] = "string";
                var enumArray = new JsonArray();
                foreach (var value in Enum.GetNames(propType))
                {
                    enumArray.Add(value);
                }
                schema["enum"] = enumArray;
                return schema;
            }

            // Handle arrays/lists
            if (propType != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(propType))
            {
                schema["type"] = "array";

                var elementType = propType.IsArray
                    ? propType.GetElementType()
                    : propType.GetGenericArguments().FirstOrDefault();

                if (elementType != null)
                {
                    schema["items"] = GenerateTypeSchema(elementType);
                }

                // Check for MinLength and MaxLength on the collection
                var minLengthAttr = prop.GetCustomAttribute<MinLengthAttribute>();
                var maxLengthAttr = prop.GetCustomAttribute<MaxLengthAttribute>();

                if (minLengthAttr != null)
                    schema["minItems"] = minLengthAttr.Length;
                if (maxLengthAttr != null)
                    schema["maxItems"] = maxLengthAttr.Length;

                return schema;
            }

            // Handle basic types
            schema = GenerateTypeSchema(propType);

            // For strings, check MinLength and MaxLength
            if (propType == typeof(string))
            {
                var minLengthAttr = prop.GetCustomAttribute<MinLengthAttribute>();
                var maxLengthAttr = prop.GetCustomAttribute<MaxLengthAttribute>();

                if (minLengthAttr != null)
                    schema["minLength"] = minLengthAttr.Length;
                if (maxLengthAttr != null)
                    schema["maxLength"] = maxLengthAttr.Length;
            }

            return schema;
        }

        private static JsonObject GenerateTypeSchema(Type type)
        {
            var schema = new JsonObject();

            if (type == typeof(string))
            {
                schema["type"] = "string";
            }
            else if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            {
                schema["type"] = "integer";
            }
            else if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                schema["type"] = "number";
            }
            else if (type == typeof(bool))
            {
                schema["type"] = "boolean";
            }
            else if (type.IsClass && type != typeof(object))
            {
                // Nested object - recursively generate schema
                schema["type"] = "object";
                var nestedProperties = new JsonObject();

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    nestedProperties[prop.Name] = GeneratePropertySchema(prop);
                }

                schema["properties"] = nestedProperties;
            }
            else
            {
                schema["type"] = "string"; // fallback
            }

            return schema;
        }
    }
}