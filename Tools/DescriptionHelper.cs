using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit
{

    public static class DescriptionHelper
    {
        public static Dictionary<string, string> GetAllDescriptionsRecursive<T>(string prefix = "")
        {
            return GetAllDescriptionsRecursive(typeof(T), prefix);
        }

        private static Dictionary<string, string> GetAllDescriptionsRecursive(Type type, string prefix = "")
        {
            var descriptions = new Dictionary<string, string>();
            var properties = type.GetProperties();

            foreach (var property in properties)
            {
                var propertyPath = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                var descriptionAttribute = property.GetCustomAttribute<DescriptionAttribute>();
                if (descriptionAttribute != null)
                {
                    descriptions[propertyPath] = descriptionAttribute.Description;
                }
                // Check if this property is a complex type that might have descriptions
                var propertyType = property.PropertyType;
                // Handle Lists
                if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    propertyType = propertyType.GetGenericArguments()[0];
                }
                // If it's a custom class, recursion
                if (propertyType.IsClass && propertyType != typeof(string) && !propertyType.IsPrimitive && !propertyType.IsEnum)
                {
                    var nestedDescriptions = GetAllDescriptionsRecursive(propertyType, propertyPath);
                    foreach (var nested in nestedDescriptions)
                    {
                        descriptions[nested.Key] = nested.Value;
                    }
                }
            }
            return descriptions;
        }

        public static string GetPropertyDescription<T>(string propertyName)
        {
            var property = typeof(T).GetProperty(propertyName);
            if (property == null) return string.Empty;

            var descriptionAttribute = property.GetCustomAttribute<DescriptionAttribute>();
            return descriptionAttribute?.Description ?? string.Empty;
        }

        public static Dictionary<string, string> GetAllDescriptions<T>()
        {
            var descriptions = new Dictionary<string, string>();
            var properties = typeof(T).GetProperties();

            foreach (var property in properties)
            {
                var descriptionAttribute = property.GetCustomAttribute<DescriptionAttribute>();
                if (descriptionAttribute != null)
                {
                    descriptions[property.Name] = descriptionAttribute.Description;
                }
            }

            return descriptions;
        }
    }
}
