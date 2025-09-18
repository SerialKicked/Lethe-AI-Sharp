using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.Files
{

    public class BaseFile : IFile
    {
        public string UniqueName { get; set; } = string.Empty;

        public virtual T? Copy<T>() where T : BaseFile
        {
            var res = MemberwiseClone() as T;
            if (res != null)
                res.UniqueName = UniqueName;
            return res;
        }
    }
}
