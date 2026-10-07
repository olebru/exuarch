using System;
using System.Threading.Tasks;

namespace Exuarch.Web.Workbench
{
    // A question before something is thrown away: the message, the button's label, and what it does.
    public sealed record Confirmation(string Message, string Action, Func<Task> Run);
}
