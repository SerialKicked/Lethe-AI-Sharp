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
        public static string Convert(string jsonSchema, string rootRuleName = "root")
        {
            var definedRules = new HashSet<string>();
            var output = new StringBuilder();

            var schema = JsonNode.Parse(jsonSchema) ?? throw new ArgumentException("Invalid JSON schema");
            GenerateRule(rootRuleName, schema, definedRules, output);

            // Add common utility rules at the end
            AppendUtilityRules(definedRules, output);

            return output.ToString();
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


        private static void GenerateRule(string ruleName, JsonNode? schema, HashSet<string> definedRules, StringBuilder output)
        {
            if (definedRules.Contains(ruleName))
                return;

            definedRules.Add(ruleName);

            var type = schema?["type"]?.GetValue<string>();

            if (type == "object")
            {
                GenerateObjectRule(ruleName, schema, definedRules, output);
            }
            else if (type == "array")
            {
                GenerateArrayRule(ruleName, schema, definedRules, output);
            }
            else if (type == "string")
            {
                GenerateStringRule(ruleName, schema, output);
            }
            else if (type == "number" || type == "integer")
            {
                GenerateNumberRule(ruleName, type == "integer", output);
            }
            else if (type == "boolean")
            {
                output.AppendLine($"{ruleName} ::= (\"true\" | \"false\") space");
            }
            else if (type == "null")
            {
                output.AppendLine($"{ruleName} ::= null");
            }
            else
            {
                // Fallback for unknown types
                output.AppendLine($"{ruleName} ::= string");
            }
        }

        private static void GenerateObjectRule(string ruleName, JsonNode? schema, HashSet<string> definedRules, StringBuilder output)
        {
            var properties = schema?["properties"]?.AsObject();
            var required = schema?["required"]?.AsArray()
                .Select(n => n!.GetValue<string>())
                .ToHashSet() ?? [];

            if (properties == null || properties.Count == 0)
            {
                output.AppendLine($"{ruleName} ::= \"{{\" space \"}}\" space");
                return;
            }

            // Build the main object rule
            output.Append($"{ruleName} ::= \"{{\" space ");

            var props = properties.ToList();
            for (int i = 0; i < props.Count; i++)
            {
                var propName = props[i].Key;
                var kvRuleName = $"{SanitizeRuleName(propName)}-kv";

                // Reference the key-value pair rule
                output.Append(kvRuleName);

                // Add comma if not last property
                if (i < props.Count - 1)
                {
                    output.Append(" \",\" space ");
                }
            }

            output.AppendLine(" \"}\" space");

            // Generate the key-value rules and value rules for each property separately
            foreach (var prop in props)
            {
                var propName = prop.Key;
                var propSchema = prop.Value;
                var valueRuleName = SanitizeRuleName(propName);
                var kvRuleName = $"{valueRuleName}-kv";
                var propType = propSchema?["type"]?.GetValue<string>();

                // For primitive types, inline them directly in the key-value rule
                if (propType == "integer")
                {
                    output.AppendLine($"{kvRuleName} ::= \"\\\"{EscapeString(propName)}\\\"\" space \":\" space integer");
                }
                else if (propType == "number")
                {
                    output.AppendLine($"{kvRuleName} ::= \"\\\"{EscapeString(propName)}\\\"\" space \":\" space number");
                }
                else if (propType == "boolean")
                {
                    output.AppendLine($"{kvRuleName} ::= \"\\\"{EscapeString(propName)}\\\"\" space \":\" space (\"true\" | \"false\") space");
                }
                else if (propType == "null")
                {
                    output.AppendLine($"{kvRuleName} ::= \"\\\"{EscapeString(propName)}\\\"\" space \":\" space null");
                }
                else
                {
                    // For complex types, generate the value rule first and reference it
                    GenerateRule(valueRuleName, propSchema, definedRules, output);
                    output.AppendLine($"{kvRuleName} ::= \"\\\"{EscapeString(propName)}\\\"\" space \":\" space {valueRuleName}");
                }
            }
        }

        private static void GenerateArrayRule(string ruleName, JsonNode? schema, HashSet<string> definedRules, StringBuilder output)
        {
            var items = schema?["items"];
            var minItems = schema?["minItems"]?.GetValue<int>();
            var maxItems = schema?["maxItems"]?.GetValue<int>();

            if (items == null)
            {
                output.AppendLine($"{ruleName} ::= \"[\" space \"]\" space");
                return;
            }

            var itemRuleName = $"{ruleName}-item";
            GenerateRule(itemRuleName, items, definedRules, output);

            // Build the array rule with min/max constraints
            if (minItems.HasValue || maxItems.HasValue)
            {
                int min = minItems ?? 0;
                int max = maxItems ?? int.MaxValue;

                if (min == 0 && max == int.MaxValue)
                {
                    // No constraints - standard rule
                    output.AppendLine($"{ruleName} ::= \"[\" space ({itemRuleName} (space \",\" space {itemRuleName})*)? space \"]\" space");
                }
                else if (min == max)
                {
                    // Exact count
                    output.Append($"{ruleName} ::= \"[\" space ");
                    for (int i = 0; i < min; i++)
                    {
                        if (i > 0) output.Append(" space \",\" space ");
                        output.Append(itemRuleName);
                    }
                    output.AppendLine(" space \"]\" space");
                }
                else if (max == int.MaxValue)
                {
                    // Only minimum
                    output.Append($"{ruleName} ::= \"[\" space ");
                    if (min == 0)
                    {
                        output.AppendLine($"({itemRuleName} (space \",\" space {itemRuleName})*)? space \"]\" space");
                    }
                    else
                    {
                        for (int i = 0; i < min; i++)
                        {
                            if (i > 0) output.Append(" space \",\" space ");
                            output.Append(itemRuleName);
                        }
                        output.AppendLine($" (space \",\" space {itemRuleName})* space \"]\" space");
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
                            options.Add(string.Join(" space \",\" space ", parts));
                        }
                    }

                    if (options.Count == 1 && string.IsNullOrEmpty(options[0]))
                    {
                        output.AppendLine($"{ruleName} ::= \"[\" space \"]\" space");
                    }
                    else
                    {
                        output.AppendLine($"{ruleName} ::= \"[\" space ({string.Join(" | ", options.Where(o => !string.IsNullOrEmpty(o)))}) space \"]\" space");
                    }
                }
            }
            else
            {
                // No constraints - standard rule
                output.AppendLine($"{ruleName} ::= \"[\" space ({itemRuleName} (space \",\" space {itemRuleName})*)? space \"]\" space");
            }
        }

        private static void GenerateStringRule(string ruleName, JsonNode? schema, StringBuilder output)
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

                output.AppendLine($"{ruleName} ::= {string.Join(" | ", options)} space");
            }
            else if (minLength.HasValue || maxLength.HasValue)
            {
                // String with length constraints
                int min = minLength ?? 0;
                int max = maxLength ?? int.MaxValue;

                string charPattern = "char";

                if (max == int.MaxValue)
                {
                    // Only minimum length
                    if (min == 0)
                    {
                        output.AppendLine($"{ruleName} ::= string | null");
                    }
                    else
                    {
                        string minChars = string.Join(" ", Enumerable.Repeat(charPattern, min));
                        output.AppendLine($"{ruleName} ::= \"\\\"\" {minChars} {charPattern}* \"\\\"\" space");
                    }
                }
                else
                {
                    // Both min and max (simplified approach)
                    if (min == 0)
                    {
                        // 0 to max - just use standard string (hard to enforce max in GBNF without explosion)
                        output.AppendLine($"{ruleName} ::= string | null");
                    }
                    else
                    {
                        // At least enforce minimum
                        string minChars = string.Join(" ", Enumerable.Repeat(charPattern, min));
                        output.AppendLine($"{ruleName} ::= \"\\\"\" {minChars} {charPattern}* \"\\\"\" space");
                    }
                }
            }
            else
            {
                // Regular string - can be nullable
                output.AppendLine($"{ruleName} ::= string | null");
            }
        }

        private static void GenerateNumberRule(string ruleName, bool isInteger, StringBuilder output)
        {
            if (isInteger)
            {
                output.AppendLine($"{ruleName} ::= integer");
            }
            else
            {
                output.AppendLine($"{ruleName} ::= number");
            }
        }

        private static void AppendUtilityRules(HashSet<string> definedRules, StringBuilder output)
        {
            if (!definedRules.Contains("space"))
            {
                output.AppendLine();
                output.AppendLine("char ::= [^\"\\\\\\x7F\\x00-\\x1F] | [\\\\] ([\"\\\\bfnrt] | \"u\" [0-9a-fA-F]{4})");
                output.AppendLine("integer ::= (\"-\"? integral-part) space");
                output.AppendLine("integral-part ::= [0] | [1-9] [0-9]{0,15}");
                output.AppendLine("null ::= \"null\" space");
                output.AppendLine("number ::= (\"-\"? integral-part) (\".\" [0-9]+)? ([eE] [-+]? [0-9]+)? space");
                output.AppendLine("space ::= | \" \" | \"\\n\"{1,2} [ \\t]{0,20}");
                output.Append("string ::= \"\\\"\" char* \"\\\"\" space");
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