using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlineAnnounce
{
    public class Config
    {
        public List<string> badwords = new List<string>()
        {
            "admin",
            "mod",
            "staff",
            "owner"
        };

        public int defaultR = 127;
        public int defaultG = 255;
        public int defaultB = 212;

        public void Write(string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static Config Read(string path)
        {
            return !File.Exists(path)
                ? new Config()
                : JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
        }
    }
}
