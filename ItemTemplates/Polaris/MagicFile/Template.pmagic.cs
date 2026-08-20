using System.Threading;
using System.Threading.Tasks;
using Polaris.Magic.Runtime;

namespace $rootnamespace$
{
    public sealed partial class $fileinputname$
    {
        /// <summary>
        /// 一次施法只调用一次。这个 Task 存活期间魔法就在运行；它不管以什么方式退出
        /// （正常完成、取消、抛异常）都代表魔法立即结束，收尾由 PolarisMagic 负责。
        /// </summary>
        private Task RunAsync(
            MagicRuntimeContext context,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
