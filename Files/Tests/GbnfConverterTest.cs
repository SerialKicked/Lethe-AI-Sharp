using System;
using System.ComponentModel.DataAnnotations;
using LetheAISharp;

namespace LetheAISharp.Files.Tests
{
    /// <summary>
    /// Test class to verify GbnfConverter functionality
    /// </summary>
    public static class GbnfConverterTest
    {
        // Test model classes
        public class SimpleObject
        {
            [Required]
            public string Name { get; set; } = string.Empty;
            [Required]
            public int Age { get; set; }
        }

        public class ObjectWithArray
        {
            [Required]
            public string Title { get; set; } = string.Empty;
            [Required]
            public string[] Items { get; set; } = Array.Empty<string>();
        }

        public class ComplexObject
        {
            [Required]
            public string Name { get; set; } = string.Empty;
            [Required]
            public int Age { get; set; }
            public string? Description { get; set; }
            public bool IsActive { get; set; }
        }

        public class NestedObject
        {
            [Required]
            public string Title { get; set; } = string.Empty;
            [Required]
            public string[] Tags { get; set; } = Array.Empty<string>();
            public ComplexObject? Metadata { get; set; }
        }

        public enum Status { Active, Inactive, Pending }

        public class ObjectWithEnum
        {
            [Required]
            public Status CurrentStatus { get; set; }
        }

        /// <summary>
        /// Test that property rules are defined separately from the main rule
        /// </summary>
        public static void TestPropertyRulesDefinedSeparately()
        {
            var grammar = GbnfConverter.Convert(typeof(SimpleObject));

            // Check that the root rule is on a single line
            var lines = grammar.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var rootLine = Array.Find(lines, line => line.StartsWith("root ::="));

            if (rootLine == null)
                throw new Exception("Root rule not found");

            // The root rule should not contain another rule definition (::=) within it
            var ruleDefCount = rootLine.Split("::=").Length - 1;
            if (ruleDefCount > 1)
                throw new Exception("Root rule contains embedded rule definitions");

            // Check that property rules are defined on separate lines
            if (!grammar.Contains("Name ::= string | null"))
                throw new Exception("Property rule for Name should be defined separately");

            if (!grammar.Contains("Age-kv ::= \"\\\"Age\\\"\" space \":\" space integer"))
                throw new Exception("Property key-value rule for Age should be defined separately");

            Console.WriteLine("✓ Property rules are defined separately");
        }

        /// <summary>
        /// Test that property names are properly quoted in JSON format
        /// </summary>
        public static void TestPropertyNamesQuoted()
        {
            var grammar = GbnfConverter.Convert(typeof(SimpleObject));

            // Property names should be quoted in the GBNF grammar with new format
            // The pattern should be: "\"PropertyName\""
            if (!grammar.Contains("\"\\\"Name\\\"\""))
                throw new Exception("Property name 'Name' should be properly quoted");

            if (!grammar.Contains("\"\\\"Age\\\"\""))
                throw new Exception("Property name 'Age' should be properly quoted");

            Console.WriteLine("✓ Property names are properly quoted");
        }

        /// <summary>
        /// Test that the root rule is well-formed and on a single line
        /// </summary>
        public static void TestRootRuleWellFormed()
        {
            var grammar = GbnfConverter.Convert(typeof(SimpleObject));
            var lines = grammar.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var rootLine = Array.Find(lines, line => line.StartsWith("root ::="));

            if (rootLine == null)
                throw new Exception("Root rule not found");

            // The root rule should start with 'root ::= "{" space'
            if (!rootLine.StartsWith("root ::= \"{\" space"))
                throw new Exception("Root rule should start with proper object syntax");

            // The root rule should end with '}' space'
            if (!rootLine.TrimEnd().EndsWith("\"}\" space"))
                throw new Exception("Root rule should end with proper object syntax");

            // The root rule should contain property key-value references (not definitions)
            if (!rootLine.Contains("Name-kv") || !rootLine.Contains("Age-kv"))
                throw new Exception("Root rule should reference property key-value rules");

            Console.WriteLine("✓ Root rule is well-formed");
        }

        /// <summary>
        /// Test array rule generation
        /// </summary>
        public static void TestArrayRuleGeneration()
        {
            var grammar = GbnfConverter.Convert(typeof(ObjectWithArray));

            // Check that array rule is properly defined
            if (!grammar.Contains("Items ::= \"[\" space (Items-item (space \",\" space Items-item)*)? space \"]\" space"))
                throw new Exception("Array rule should be properly defined");

            // Check that array item rule is defined separately
            if (!grammar.Contains("Items-item ::= string | null"))
                throw new Exception("Array item rule should be defined separately");

            Console.WriteLine("✓ Array rules are properly generated");
        }

        /// <summary>
        /// Test nested object rule generation
        /// </summary>
        public static void TestNestedObjectRuleGeneration()
        {
            var grammar = GbnfConverter.Convert(typeof(NestedObject));

            // Check that nested object rule is defined
            if (!grammar.Contains("Metadata ::="))
                throw new Exception("Nested object rule should be defined");

            // Check that nested object properties are defined
            if (!grammar.Contains("Name ::= string | null"))
                throw new Exception("Nested object property rule should be defined");

            if (!grammar.Contains("Age-kv ::= \"\\\"Age\\\"\" space \":\" space integer"))
                throw new Exception("Nested object property key-value rule should be defined");

            Console.WriteLine("✓ Nested object rules are properly generated");
        }

        /// <summary>
        /// Test enum rule generation
        /// </summary>
        public static void TestEnumRuleGeneration()
        {
            var grammar = GbnfConverter.Convert(typeof(ObjectWithEnum));

            // Check that enum values are properly listed with space suffix
            if (!grammar.Contains("CurrentStatus ::= \"Active\" | \"Inactive\" | \"Pending\" space"))
                throw new Exception("Enum rule should list all enum values");

            Console.WriteLine("✓ Enum rules are properly generated");
        }

        /// <summary>
        /// Test that utility rules are appended
        /// </summary>
        public static void TestUtilityRulesAppended()
        {
            var grammar = GbnfConverter.Convert(typeof(SimpleObject));

            // Check that utility rules are present (now using 'space' instead of 'ws')
            if (!grammar.Contains("space ::= | \" \" | \"\\n\"{1,2} [ \\t]{0,20}"))
                throw new Exception("Space rule should be defined");

            if (!grammar.Contains("string ::="))
                throw new Exception("String rule should be defined");

            if (!grammar.Contains("integer ::="))
                throw new Exception("Integer rule should be defined");

            if (!grammar.Contains("char ::="))
                throw new Exception("Char rule should be defined");

            if (!grammar.Contains("null ::="))
                throw new Exception("Null rule should be defined");

            Console.WriteLine("✓ Utility rules are properly appended");
        }

        /// <summary>
        /// Test that grammar has no malformed line breaks
        /// </summary>
        public static void TestNoMalformedLineBreaks()
        {
            var grammar = GbnfConverter.Convert(typeof(ComplexObject));
            var lines = grammar.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Each rule should be on its own line
            // Check that lines starting with a rule name have proper format
            foreach (var line in lines)
            {
                if (line.Contains("::=") && !line.StartsWith("#"))
                {
                    // Count occurrences of ::= in the line
                    var defCount = line.Split("::=").Length - 1;
                    if (defCount > 1)
                        throw new Exception($"Line contains multiple rule definitions: {line}");
                }
            }

            Console.WriteLine("✓ No malformed line breaks detected");
        }

        /// <summary>
        /// Test handling of boolean types
        /// </summary>
        public static void TestBooleanTypeHandling()
        {
            var grammar = GbnfConverter.Convert(typeof(ComplexObject));

            // Boolean should be represented as true/false alternatives with space suffix, inlined in key-value rule
            if (!grammar.Contains("IsActive-kv ::= \"\\\"IsActive\\\"\" space \":\" space (\"true\" | \"false\") space"))
                throw new Exception("Boolean rule should list true and false as alternatives with space suffix");

            Console.WriteLine("✓ Boolean types are properly handled");
        }

        /// <summary>
        /// Test that optional properties are handled correctly
        /// </summary>
        public static void TestOptionalPropertiesHandling()
        {
            var grammar = GbnfConverter.Convert(typeof(ComplexObject));

            // All properties (required and optional) should be in the grammar
            // Note: Current implementation includes all properties regardless of Required attribute
            if (!grammar.Contains("Description ::= string | null"))
                throw new Exception("Optional property should still be defined in grammar");

            Console.WriteLine("✓ Optional properties are properly handled");
        }

        /// <summary>
        /// Run all GBNF converter tests
        /// </summary>
        public static void RunAllTests()
        {
            Console.WriteLine("Running GBNF Converter Tests...");
            Console.WriteLine();

            TestPropertyRulesDefinedSeparately();
            TestPropertyNamesQuoted();
            TestRootRuleWellFormed();
            TestArrayRuleGeneration();
            TestNestedObjectRuleGeneration();
            TestEnumRuleGeneration();
            TestUtilityRulesAppended();
            TestNoMalformedLineBreaks();
            TestBooleanTypeHandling();
            TestOptionalPropertiesHandling();

            Console.WriteLine();
            Console.WriteLine("✓ All GBNF Converter tests passed!");
        }
    }
}
