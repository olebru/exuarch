using System;

namespace Exuarch.Web
{
    public class HelpService
    {
        public event Action<string, string> Requested;

        public void Open(string kind, string target)
        {
            Requested?.Invoke(kind, target);
        }
    }
}
