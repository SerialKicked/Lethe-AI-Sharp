using System;
using System.IO;
using AIToolkit.Files;
using AIToolkit.Memory;
using Newtonsoft.Json;

namespace AIToolkitTest
{
    // Test class to simulate old persona format with embedded Brain
    public class OldPersonaFormat
    {
        public string UniqueName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public Brain Brain { get; set; } = new();
    }
    
    class Program
    {
        static void Main()
        {
            // Create a test directory
            var testDir = "/tmp/test_data/chars/";
            Directory.CreateDirectory(testDir);
            
            Console.WriteLine("=== Testing Brain Separation ===");
            
            // Test 1: Create new persona and verify brain separation
            Console.WriteLine("\n1. Testing new persona creation...");
            var persona = new BasePersona()
            {
                UniqueName = "TestChar",
                Name = "Test Character",
                Bio = "A test character"
            };
            
            // Add some brain data
            persona.Brain.MinInsertDelay = TimeSpan.FromMinutes(20);
            persona.Brain.MinMessageDelay = 7;
            
            // Save persona (should save brain separately)
            persona.EndChat(false);
            (persona as IFile).SaveToFile(testDir + "TestChar.json");
            
            // Verify brain file was created
            var brainFile = testDir + "TestChar.brain";
            if (File.Exists(brainFile))
            {
                Console.WriteLine("✓ Brain file created successfully");
                var brainContent = File.ReadAllText(brainFile);
                Console.WriteLine("  Brain file size: " + brainContent.Length + " characters");
            }
            else
            {
                Console.WriteLine("✗ ERROR: Brain file not created");
                return;
            }
            
            // Verify persona file doesn't contain brain data
            var personaFile = testDir + "TestChar.json";
            if (File.Exists(personaFile))
            {
                var personaContent = File.ReadAllText(personaFile);
                if (personaContent.Contains("\"Brain\""))
                {
                    Console.WriteLine("✗ ERROR: Persona file still contains Brain data");
                    return;
                }
                else
                {
                    Console.WriteLine("✓ Persona file doesn't contain Brain data");
                }
            }
            
            // Test 2: Load brain data
            Console.WriteLine("\n2. Testing brain loading...");
            var newPersona = new BasePersona() { UniqueName = "TestChar" };
            newPersona.BeginChat();
            
            if (newPersona.Brain.MinInsertDelay == TimeSpan.FromMinutes(20) && 
                newPersona.Brain.MinMessageDelay == 7)
            {
                Console.WriteLine("✓ Brain data loaded correctly");
            }
            else
            {
                Console.WriteLine("✗ ERROR: Brain data not loaded correctly");
                return;
            }
            
            // Test 3: Migration from old format
            Console.WriteLine("\n3. Testing migration from old format...");
            
            // Create an old-format persona file
            var oldPersona = new OldPersonaFormat()
            {
                UniqueName = "OldChar",
                Name = "Old Character",
                Bio = "An old character",
                Brain = new Brain()
                {
                    MinInsertDelay = TimeSpan.FromMinutes(30),
                    MinMessageDelay = 10
                }
            };
            
            var oldPersonaJson = JsonConvert.SerializeObject(oldPersona, new JsonSerializerSettings { Formatting = Formatting.Indented });
            File.WriteAllText(testDir + "OldChar.json", oldPersonaJson);
            
            // Now try to load it with the new system
            var migratedPersona = new BasePersona() { UniqueName = "OldChar" };
            migratedPersona.BeginChat();
            
            if (migratedPersona.Brain.MinInsertDelay == TimeSpan.FromMinutes(30) && 
                migratedPersona.Brain.MinMessageDelay == 10)
            {
                Console.WriteLine("✓ Migration from old format successful");
                
                // Verify that brain file was created during migration
                if (File.Exists(testDir + "OldChar.brain"))
                {
                    Console.WriteLine("✓ Brain file created during migration");
                }
                else
                {
                    Console.WriteLine("✗ ERROR: Brain file not created during migration");
                }
            }
            else
            {
                Console.WriteLine("✗ ERROR: Migration failed");
            }
            
            Console.WriteLine("\n=== All tests completed ===");
        }
    }
}