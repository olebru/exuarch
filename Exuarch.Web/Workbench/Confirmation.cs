using System;
using System.Threading.Tasks;

namespace Exuarch.Web.Workbench
{
    public sealed record Confirmation(string Message, string Action, Func<Task> Run);
}
