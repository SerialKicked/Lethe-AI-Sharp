using AIToolkit.Files.Tests;
using System;

// Simple test runner to validate GroupPersona changes
class Program
{
    static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("Testing GroupPersona Implementation Fixes...");
            Console.WriteLine("=" + new string('=', 50));
            
            GroupChatTest.RunAllTests();
            
            Console.WriteLine("=" + new string('=', 50));
            Console.WriteLine("All tests completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Environment.Exit(1);
        }
    }
}