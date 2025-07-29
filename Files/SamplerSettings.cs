using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIToolkit.API;

namespace AIToolkit.Files
{
    public class SamplerSettings : GenerationInput, IFile
    {
        public string Description { get; set; } = string.Empty;
        public string UniqueName { get; set; } = string.Empty;

        // Constructor with default values
        public SamplerSettings()
        {
            Description = "Default sampling settings. This should work with most models out of the box.";
            UniqueName = "Default";
            Prompt = "";
            Max_context_length = 4096;
            Max_length = 512;
            Temperature = 0.7;
            Top_k = 0;
            Top_p = 1;
            Typical = 1;
            Min_p = 0;
            Top_a = 0;
            Tfs = 1;
            Rep_pen = 1;
            Rep_pen_range = 0;
            Smoothing_factor = 0;
            Xtc_threshold = 0.1;
            Xtc_probability = 0.33;
            Dry_allowed_length = 2;
            Dry_base = 1.75;
            Dry_multiplier = 0.8;
            Dry_allowed_length = 2;
            Dry_sequence_breakers = ["\n", ":", "\"", "*", "<|im_end|>", "<|im_start|>" ];
            Sampler_order = [6, 0, 1, 3, 4, 2, 5];
            Mirostat = 0;
            Mirostat_eta = 0.1;
            Mirostat_tau = 5;
            this.Banned_tokens = [];
            this.Bypass_eos = false;
            this.Sampler_seed = -1;
            this.Trim_stop = false;
        }

        public SamplerSettings GetCopy()
        {
            var res = (this.MemberwiseClone() as SamplerSettings)!;
            res.UniqueName = UniqueName;
            // Remove duplicates from dry_sequence_breakers
            res.Dry_sequence_breakers = res.Dry_sequence_breakers.Distinct().ToList();
            return res;
        }
    }
}
