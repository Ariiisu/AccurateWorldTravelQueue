using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AccurateWorldTravelQueue.Utils;
using Advanced_Combat_Tracker;

namespace AccurateWorldTravelQueue
{
    public class PluginMain : IActPluginV1
    {
        private Memory _memory;

        internal PluginPage configPage;

        // ACT界面(插件列表中)的状态标签
        private Label lblStatus;

        /// <summary>
        ///     插件初始化
        /// </summary>
        /// <param name="pluginScreenSpace"></param>
        /// <param name="pluginStatusText"></param>
        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            Helpers._tabPage = pluginScreenSpace;
            lblStatus = pluginStatusText;
            pluginScreenSpace.Text = "显示实际跨服排队顺序";

            Helpers._plugin = this;

            //Helpers._configPage = configPage = new PluginPage();
            //configPage.Dock = DockStyle.Fill;
            //pluginScreenSpace.Controls.Add(configPage);
            //移除不需要的插件UI。
            ((TabControl)(pluginScreenSpace.Parent)).TabPages.Remove(pluginScreenSpace);

            // 初始化
            Attach();

            lblStatus.Text = "初始化完成";
        }

        /// <summary>
        ///     插件反初始化
        /// </summary>
        public void DeInitPlugin()
        {
            Helpers.ffxivPlugin.DataSubscription.ProcessChanged -= OnFFXIVProcessChanged;
            //settings.save();
            lblStatus.Text = "插件已卸载";
        }

        /// <summary>
        ///     附加到解析插件
        /// </summary>
        public void Attach()
        {
            lock (this)
            {
                if (ActGlobals.oFormActMain == null)
                {
                    Helpers.ffxivPlugin = null;
                    return;
                }

                if (Helpers.ffxivPlugin == null)
                {
                    var ffxivPluginData = ActGlobals.oFormActMain.ActPlugins.FirstOrDefault(x => x.pluginObj?.GetType().ToString() == "FFXIV_ACT_Plugin.FFXIV_ACT_Plugin");
                    Helpers.ffxivPlugin = (FFXIV_ACT_Plugin.FFXIV_ACT_Plugin)ffxivPluginData?.pluginObj;
                    if (Helpers.ffxivPlugin != null)
                    {
                        var waitingFFXIVPlugin = new Task(() =>
                        {
                            var isFFXIVPluginStarted = false;
                            while (!isFFXIVPluginStarted)
                            {
                                if (ffxivPluginData.lblPluginStatus.Text.ToUpper().Contains("Started".ToUpper()))
                                {
                                    Helpers.ffxivPlugin.DataSubscription.ProcessChanged += OnFFXIVProcessChanged;
                                    OnFFXIVProcessChanged(Helpers.ffxivPlugin.DataRepository.GetCurrentFFXIVProcess());
                                    return;
                                }

                                Thread.Sleep(3000);
                            }
                        });
                        waitingFFXIVPlugin.Start();
                    }
                }
            }
        }

        /// <summary>
        ///     当客户端进程变化时触发操作
        /// </summary>
        /// <param name="process"></param>
        private void OnFFXIVProcessChanged(Process process)
        {
            var gameProcess = process;
            if (gameProcess == null)
                return;

            try
            {
                if (_memory == null)
                    _memory = new Memory(process);
                else
                    _memory.UpdateProcess(process);
            }
            catch (Exception e)
            {
                //避免用户迷惑错误，插件需要声明错误来源。
                Task.Run(() => MessageBox.Show($"修改内存失败。原因: {e}", "显示实际跨服顺序"));
            }
        }
    }
}
