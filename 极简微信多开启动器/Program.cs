using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;
using System.Drawing;
using Microsoft.Win32;
using System.Threading;
using System.Runtime.InteropServices;

namespace 极简微信多开启动器
{
    internal class Program
    {
        // 互斥体名称
        private const string WeChatMutex3 = "_WeChat_App_Instance_Identity_Mutex_Name";
        private const string WeChatMutex4 = "XWeChat_App_Instance_Identity_Mutex_Name";
        
        // Windows API导入
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        // Windows消息常量
        private const uint WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        
        // 获取应用程序图标
        private static Icon GetAppIcon()
        {
            try
            {
                // 从内置资源加载图标
                return Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
                // 如果无法加载自定义图标，返回默认图标
                return SystemIcons.Application;
            }
        }
        
        // 设置窗口图标
        private static void SetWindowIcon(Form form)
        {
            try
            {
                Icon appIcon = GetAppIcon();
                form.Icon = appIcon;
            }
            catch { }
        }

        static void Main(string[] args)
        {
            try
            {
                // 确保应用程序使用内置图标
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                // 检测微信安装位置
                string weChatPath = GetWeChatPath();
                if (string.IsNullOrEmpty(weChatPath))
                {
                    ShowError("未找到微信安装位置，请使用官方版本微信。");
                    return;
                }

                // 确定需要屏蔽的互斥体
                bool hasWeChat3 = CheckWeChatInstallation("SOFTWARE\\Tencent\\WeChat");
                bool hasWeChat4 = CheckWeChatInstallation("SOFTWARE\\Tencent\\Weixin");

                // 创建并屏蔽互斥体
                bool mutexBlocked = false;
                if (hasWeChat4)
                {
                    // 优先屏蔽微信4的互斥体
                    mutexBlocked = BlockMutexAccess(WeChatMutex4);
                }
                if (hasWeChat3 && !mutexBlocked)
                {
                    // 如果微信4的互斥体屏蔽失败或未安装微信4，尝试屏蔽微信3的互斥体
                    mutexBlocked = BlockMutexAccess(WeChatMutex3);
                }

                if (!mutexBlocked)
                {
                    // 如果都没有屏蔽成功，尝试屏蔽所有可能的互斥体
                    mutexBlocked = BlockMutexAccess(WeChatMutex3) || BlockMutexAccess(WeChatMutex4);
                }

                if (!mutexBlocked)
                {
                    // 如果仍然失败，提示权限问题
                    ShowError("无法屏蔽微信互斥体，请以管理员身份运行此程序。");
                    return;
                }

                // 启动微信
                StartWeChat(weChatPath);

                // 短暂等待，确保微信进程已启动
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                ShowError("启动微信时出错：" + ex.Message);
            }
            finally
            {
                // 程序自动退出，无需用户交互
            }
        }

        private static string GetWeChatPath()
        {
            // 优先查找微信4
            string weChat4Path = GetWeChatPathFromRegistry("SOFTWARE\\Tencent\\Weixin", "Weixin.exe");
            if (!string.IsNullOrEmpty(weChat4Path))
            {
                return weChat4Path;
            }

            // 然后查找微信3
            string weChat3Path = GetWeChatPathFromRegistry("SOFTWARE\\Tencent\\WeChat", "WeChat.exe");
            if (!string.IsNullOrEmpty(weChat3Path))
            {
                return weChat3Path;
            }

            return null;
        }

        private static string GetWeChatPathFromRegistry(string registryPath, string exeName)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath))
                {
                    if (key != null)
                    {
                        string installPath = key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(installPath))
                        {
                            string exePath = Path.Combine(installPath, exeName);
                            if (File.Exists(exePath))
                            {
                                return exePath;
                            }
                        }
                    }

                    // 尝试从LocalMachine查找
                    using (RegistryKey machineKey = Registry.LocalMachine.OpenSubKey(registryPath))
                    {
                        if (machineKey != null)
                        {
                            string installPath = machineKey.GetValue("InstallPath") as string;
                            if (!string.IsNullOrEmpty(installPath))
                            {
                                string exePath = Path.Combine(installPath, exeName);
                                if (File.Exists(exePath))
                                {
                                    return exePath;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 忽略注册表访问错误
            }
            return null;
        }

        private static bool CheckWeChatInstallation(string registryPath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath))
                {
                    if (key != null)
                    {
                        return true;
                    }
                }
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath))
                {
                    if (key != null)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // 忽略注册表访问错误
            }
            return false;
        }

        private static bool BlockMutexAccess(string mutexName)
        {
            try
            {
                // 创建严格的安全权限，禁止任何人（包括当前用户）访问互斥体
                MutexSecurity mutexSecurity = new MutexSecurity();

                // 拒绝所有用户的所有权限
                MutexAccessRule denyRule = new MutexAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    MutexRights.FullControl,
                    AccessControlType.Deny);
                mutexSecurity.AddAccessRule(denyRule);

                // 尝试关闭已存在的同名互斥体
                try
                {
                    Mutex existingMutex = Mutex.OpenExisting(mutexName);
                    existingMutex?.Close();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // 互斥体不存在，这是正常情况
                }
                catch (UnauthorizedAccessException)
                {
                    // 没有足够权限访问互斥体
                    // 在.NET Framework中无法以更低权限打开，继续执行
                }

                // 使用正确的方式创建互斥体并设置安全权限
                bool createdNew;
                Mutex mutex = new Mutex(true, mutexName, out createdNew);

                // 设置互斥体的安全描述符，使其他进程无法访问
                try
                {
                    mutex.SetAccessControl(mutexSecurity);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void StartWeChat(string weChatPath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = weChatPath,
                UseShellExecute = true,
                CreateNoWindow = false
            };
            Process.Start(startInfo);
        }

        private static void ShowError(string message)
        {
            // 使用自定义的错误信息框，包含应用程序图标
            using (Form errorForm = new Form())
            {
                // 设置表单属性
                errorForm.Size = new Size(100, 100);
                errorForm.StartPosition = FormStartPosition.CenterScreen;
                errorForm.ShowInTaskbar = false;
                
                // 设置表单图标
                SetWindowIcon(errorForm);
                
                // 显示带有应用程序图标的消息框
                System.Windows.Forms.MessageBox.Show(
                    errorForm,
                    message,
                    "错误",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }
    }
}