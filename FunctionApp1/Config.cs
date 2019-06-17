using System;

namespace FunctionApp1
{
    static class Config
    {
        public static string ComputerVisionHost { get; private set; }
        public static string ComputerVisionApiKey { get; private set; } // the Ocp-Apim-Subscription-Key value

        static Config()
        {
            ComputerVisionHost = Environment.GetEnvironmentVariable("TESSIN_COMPUTER_VISION_API_HOST");
            ComputerVisionApiKey = Environment.GetEnvironmentVariable("TESSIN_COMPUTER_VISION_API_KEY");
        }
    }
}
