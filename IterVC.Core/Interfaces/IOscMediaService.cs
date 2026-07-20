using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IterVC.Core.Interfaces
{
    public interface IOscMediaService
    {
        void SendMediaInfo(string? title, string? status, string template);
        void ClearChatbox();
    }
}
