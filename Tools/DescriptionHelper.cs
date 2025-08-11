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
