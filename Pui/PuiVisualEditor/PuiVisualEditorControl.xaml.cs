using Polaris.PUI.Wire;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;
using PolarisTools.Pui.PuiVisualEditor.HotReload;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;

namespace PolarisTools.Pui.PuiVisualEditor
{
    public partial class PuiVisualEditorControl : UserControl
    {
        public PuiVisualEditorViewModel ViewModel { get; }
        private bool _propertyChangePending;
        private readonly bool _isToolWindowHost;

        public PuiVisualEditorControl() : this(isToolWindowHost: false)
        {
        }

        public PuiVisualEditorControl(bool isToolWindowHost)
        {
            _isToolWindowHost = isToolWindowHost;
            InitializeComponent();
            ViewModel = new PuiVisualEditorViewModel();
            DataContext = ViewModel;
            Loaded += (s, e) => Focus();
            if (!_isToolWindowHost)
                HideStartOverlay();

            // 切换到新选中的元素/行之后，第一次编辑应该重新起一条撤销快照，而不是继续并进
            // 切换选中之前那次编辑的快照里——不然编辑过一次之后，不管中途换选了多少次元素，
            // 撤销栈永远只有那一条越滚越大的记录。这里统一订阅一次就能覆盖画布点选、
            // 新增元素后自动选中、行选择等所有会改 SelectedElement/SelectedLine 的路径。
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PuiVisualEditorViewModel.SelectedElement)
                    || e.PropertyName == nameof(PuiVisualEditorViewModel.SelectedLine))
                    _propertyChangePending = false;
            };
        }

        /// <summary>工具窗口每次被打开时调用：重新显示「新建 / 打开现有」。</summary>
        public void ShowStartOverlay()
        {
            if (_isToolWindowHost && StartOverlay != null)
                StartOverlay.Visibility = Visibility.Visible;
        }

        public void HideStartOverlay()
        {
            if (StartOverlay != null)
                StartOverlay.Visibility = Visibility.Collapsed;
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CreateNewDocument();
            HideStartOverlay();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dlg = new OpenFileDialog
            {
                Title = "Open PUI file",
                Filter = "PUI File (*.pui)|*.pui|All files (*.*)|*.*",
                DefaultExt = ".pui",
                CheckFileExists = true,
                Multiselect = false
            };

            if (ShowFileDialog(dlg) != true)
                return;

            LoadFromFile(dlg.FileName);
            HideStartOverlay();
        }

        /// <summary>
        /// Microsoft.Win32.CommonDialog.ShowDialog() 只能接受 System.Windows.Window 类型的
        /// owner（传 null 会直接抛 ArgumentNullException），但这个控件是作为
        /// ToolWindowPane/WindowPane 的内容承载在 VS 里的，视觉树里往上找不到一个真正的 Window
        /// 祖先——不给 owner 的话，对话框有时候能创建出来（任务栏能看到一个新窗口），但因为没有
        /// "属于哪个窗口"的从属关系，撞上 Windows 的前台激活保护（没有 owner 的新窗口不能抢占
        /// 前台，只能在任务栏闪烁），表现就是"看起来什么都没发生"。这里用真正承载这个控件的
        /// HwndSource 句柄，造一个不显示、不在任务栏出现的占位 Window 当 owner——EnsureHandle()
        /// 会立刻把它的原生句柄建出来并挂到 VS 主窗口下面，不需要真的 Show() 它；如果这个控件
        /// 当前压根没有 PresentationSource（理论上不该发生，但防一手），退回不带 owner 的重载，
        /// 总比直接抛异常好。
        /// </summary>
        private bool? ShowFileDialog(CommonDialog dialog)
        {
            var owner = CreateDialogOwner();
            return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        }

        // EnvDTE 和 System.Windows 里都有一个叫 Window 的类型，这里必须写全名消歧义。
        private System.Windows.Window CreateDialogOwner()
        {
            if (!(PresentationSource.FromVisual(this) is HwndSource hwndSource))
                return null;

            var owner = new System.Windows.Window
            {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            var helper = new WindowInteropHelper(owner) { Owner = hwndSource.Handle };
            helper.EnsureHandle();
            return owner;
        }

        private void ToolboxItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ToolboxItem item)
                ViewModel.AddElement(item.Type);
        }

        /// <summary>
        /// 作为工具窗口打开时（<see cref="PuiVisualEditorWindow"/>，不是双击 .pui 文件打开的那个
        /// <see cref="PuiEditorPane"/>），这里是唯一真正会保存的入口：这个控件没有接入
        /// VS 的标准文档持久化机制，Ctrl+S / 菜单"保存"不会落到它头上（可能什么都不做，
        /// 也可能保存的是主编辑区里恰好处于焦点的其它文件），所以必须走这个按钮。
        /// </summary>
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}", "PUI Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 把当前编辑的布局打包成一份热重载指令（跟保存到磁盘的 .pui 内容无关，直接取
        /// 编辑器里的实时状态），通过命名管道推给正在运行的游戏进程。PUI 的身份用文件名
        /// （不含扩展名）确定，必须跟生成器给 <see cref="IPUI"/>.Name 用的取值方式一致，
        /// 所以必须先保存过一次才知道叫什么名字。
        /// </summary>
        private void HotReload_Click(object sender, RoutedEventArgs e)
        {
            // XAML 挂接的事件处理器不能是 async void（异常会直接崩进程且分析器看不到 XAML 里的
            // 绑定关系），改成同步方法 + 显式 FileAndForget 的 async 本地委托。VSSDK007 认的
            // "已处理"只有 Join/await，识别不出 FileAndForget，这里显式压掉。
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                if (string.IsNullOrEmpty(ViewModel.FilePath))
                {
                    MessageBox.Show("Save the .pui file before hot reloading.", "PUI Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string puiName = Path.GetFileNameWithoutExtension(ViewModel.FilePath);
                // Window.Name 不再是用户填的字段，统一用文件名本身——跟下面 SendAsync 路由用的
                // puiName 是同一个值，不会再出现"CreateWindow 发的名字"和"热重载路由的名字"对不上。
                ViewModel.RootElement.Name = puiName;

                var emitter = new PuiHotReloadEmitter();
                try
                {
                    PuiTreeWalker.Walk(ViewModel.RootElement, emitter);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"The current layout failed to parse and was not sent: {ex.Message}", "PUI Editor", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                (bool ok, string error) = await PuiHotReloadClient.SendAsync(puiName, emitter.Commands, TimeSpan.FromSeconds(3)).ConfigureAwait(true);

                if (ok)
                    MessageBox.Show($"\"{puiName}\" was hot reloaded.", "PUI Editor", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"Hot reload failed: {error}", "PUI Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }).FileAndForget("PolarisTools/HotReload");
#pragma warning restore VSSDK007
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DeleteElement(ViewModel.SelectedElement);
        }

        /// <summary>
        /// "一键创建回调"：按钮的 Tag 是 hook 种类字符串（"OnClick"/"OnChanged"/"OnChangedDelay"/
        /// "OnColorChanged"/"OnBuildCompleted"）。方法名为空时按规则生成一个
        /// （若与文档里已绑定的其他方法名冲突，会自动加 "_0"/"_1"... 后缀，见
        /// <see cref="PuiVisualEditorViewModel.EnsureUniqueCallbackName"/>），然后立刻把方法桩写进
        /// .pui.cs（不用等保存 .pui），再用 DTE 打开那个文件并把光标定位到那一行。已绑定时则只是
        /// 打开文件跳转，不会改动方法名。
        /// </summary>
        private void CreateHandler_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is not Button btn || btn.Tag is not string hookKind)
                return;

            PuiElement element = ViewModel.SelectedElement;
            if (element == null)
                return;

            if (string.IsNullOrEmpty(ViewModel.FilePath))
            {
                MessageBox.Show("Save the .pui file before creating callback methods.", "PUI Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PolarisPuiGenerator.HandlerRequirement signature = PolarisPuiGenerator.GetHandlerSignature(element.ElementType, hookKind);
            PuiCallbackHook hook = FindHook(element, hookKind);
            if (signature == null || hook == null)
                return;

            string methodName = hook.MethodName;
            if (string.IsNullOrEmpty(methodName))
            {
                methodName = ViewModel.EnsureUniqueCallbackName(GenerateDefaultHandlerName(element, hookKind));
                hook.MethodName = methodName;
                OnPropertyChanged();
            }
            signature.MethodName = methodName;

            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                ProjectItem puiItem = dte?.Solution?.FindProjectItem(ViewModel.FilePath);
                string defaultNamespace = puiItem?.ContainingProject?.Properties?.Item("DefaultNamespace")?.Value as string ?? "";

                string codeBehindPath = PuiCodeBehindSync.EnsureCodeBehindFile(ViewModel.FilePath, defaultNamespace, puiItem);
                int line = PuiCodeBehindSync.EnsureHandlerStub(codeBehindPath, signature);

                if (dte != null)
                {
                    dte.ItemOperations.OpenFile(codeBehindPath);
                    if (dte.ActiveDocument?.Selection is TextSelection selection)
                        selection.GotoLine(line, true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create the callback method: {ex.Message}", "PUI Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 按 hook 种类字符串（按钮 Tag，来自 <see cref="PuiCallbackHook.HookKind"/>）找到对应的
        /// hook 对象。读写方法名统一走 <see cref="PuiCallbackHook.MethodName"/>——每个 hook 自己就
        /// 持有"我对应 PuiElement 上哪个字段"的 getter/setter（见 PuiElement.BuildCallbackHooks），
        /// 这里不需要再抄一份 hookKind → 字段 的映射。该类型不支持这种 hook 时返回 null。
        /// </summary>
        private static PuiCallbackHook FindHook(PuiElement e, string hookKind)
        {
            foreach (PuiCallbackHook hook in e.CallbackHooks)
            {
                if (hook.HookKind == hookKind)
                    return hook;
            }
            return null;
        }

        private static string GenerateDefaultHandlerName(PuiElement e, string hookKind)
        {
            string name = string.IsNullOrEmpty(e.Name) ? e.ElementType.ToString() : e.Name;
            return hookKind switch
            {
                "OnClick" => $"On{name}Click",
                "OnChanged" => $"On{name}Changed",
                "OnChangedDelay" => $"On{name}ChangedDelay",
                "OnColorChanged" => $"On{name}ColorChanged",
                "OnBuildCompleted" => $"On{name}BuildCompleted",
                _ => $"On{name}",
            };
        }

        /// <summary>
        /// "回调"Tab 里每个 hook 行的"解绑"按钮：只清空 PuiElement 上的方法名连接，
        /// .pui.cs 里已经生成的方法体永远不会被这里删除或改动。
        /// </summary>
        private void Unbind_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string hookKind)
                return;

            PuiElement element = ViewModel.SelectedElement;
            if (element == null)
                return;

            PuiCallbackHook hook = FindHook(element, hookKind);
            if (hook == null)
                return;

            hook.MethodName = "";
            OnPropertyChanged();
        }

        // 普通 TextBox 是标准 WPF 控件，内部改不了，只能靠焦点区分：用户真的在打字时，这个
        // TextBox 一定持有键盘焦点；SelectedElement 切换导致绑定把 Text 重新赋值时，焦点早已经
        // 不在它身上了（点画布/点别的元素本身就会先把焦点移走）。没有焦点就说明这次 TextChanged
        // 是外部重新赋值触发的，不是用户编辑，不该标记为已修改。
        private void Property_Changed(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb && !tb.IsKeyboardFocusWithin) return;
            OnPropertyChanged();
        }

        private void Property_Changed(object sender, RoutedEventArgs e) => OnPropertyChanged();

        // PuiNumberBox.ValueChanged / PuiColorPicker.HexRgbaChanged 都是普通 EventHandler（不是
        // RoutedEventArgs），需要单独一个重载才能被 XAML 里的 ValueChanged="Property_Changed" /
        // HexRgbaChanged="Property_Changed" 绑上。这两个控件自己已经保证了只在用户真正编辑时才
        // 触发这个事件（见 PuiNumberBox._isUserEdit / PuiColorPicker._syncingFromHex），这里不用
        // 再额外判断焦点。
        private void Property_Changed(object sender, EventArgs e) => OnPropertyChanged();

        private void Combo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && !cb.IsKeyboardFocusWithin) return;
            OnPropertyChanged();
        }

        private void OnPropertyChanged()
        {
            if (!_propertyChangePending)
            {
                _propertyChangePending = true;
                ViewModel.SaveStateBeforePropertyChange();
            }
            ViewModel.MarkDirty();
        }

        private void NameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ViewModel.EnsureUniqueElementName(ViewModel.SelectedElement);
            ViewModel.MarkDirty();
        }

        private void UserControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.Z)
                {
                    ViewModel.Undo();
                    _propertyChangePending = false;
                    e.Handled = true;
                }
                else if (e.Key == Key.Y)
                {
                    ViewModel.Redo();
                    _propertyChangePending = false;
                    e.Handled = true;
                }
            }
        }

        private void ApplySource_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.TryParseXmlSource())
            {
                MessageBox.Show("XML parsing failed; please check the format.", "PUI Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PreviewRenderer_ElementSelected(object sender, PuiElement element)
        {
            ViewModel.SelectedElement = element;
        }

        public void LoadFromFile(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ViewModel.LoadFromFile(path);
        }

        public void SaveToFile(string path) => ViewModel.SaveToFile(path);

        public void Save()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!string.IsNullOrEmpty(ViewModel.FilePath))
            {
                ViewModel.SaveToFile(ViewModel.FilePath);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Save PUI file",
                Filter = "PUI File (*.pui)|*.pui",
                DefaultExt = ".pui",
                InitialDirectory = TryGetCurrentProjectDirectory(),
            };

            if (ShowFileDialog(dlg) == true)
            {
                ViewModel.SaveToFile(dlg.FileName);
                // 新建文档第一次保存（Save As）之后，立刻用刚写盘的文件重新加载一遍——等于把
                // 编辑器关了再打开这份文件，从用户能看到结果的角度确认这次真的落盘成功了，
                // 不用只靠内存里那份没变过的状态来"自我感觉良好"。
                LoadFromFile(dlg.FileName);
            }
        }

        /// <summary>
        /// 新建文档还没存过盘，弹的保存对话框默认目录用当前（解决方案资源管理器里选中的）
        /// 项目所在目录；拿不到就退一步用解决方案目录；再拿不到就交给对话框自己的默认值。
        /// </summary>
        private static string TryGetCurrentProjectDirectory()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                if (dte == null)
                    return null;

                if (dte.ActiveSolutionProjects is Array activeProjects && activeProjects.Length > 0
                    && activeProjects.GetValue(0) is Project project && !string.IsNullOrEmpty(project.FullName))
                {
                    return Path.GetDirectoryName(project.FullName);
                }

                if (!string.IsNullOrEmpty(dte.Solution?.FullName))
                    return Path.GetDirectoryName(dte.Solution.FullName);
            }
            catch
            {
                // 拿不到就算了，回退到 SaveFileDialog 自己的默认目录。
            }

            return null;
        }
    }

    public class ElementTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return parameter as string switch
            {
                "CanAdd" => value is PuiElementType t && t != PuiElementType.Window,
                "NullVisible" => value == null ? Visibility.Visible : Visibility.Collapsed,
                "NotNullVisible" => value != null ? Visibility.Visible : Visibility.Collapsed,
                "ShowText" => IsTextEditableElement(value) ? Visibility.Visible : Visibility.Collapsed,
                // Window 的 Name 不再是用户填的字段——生成/热重载时统一用 .pui 文件名本身
                // （见 PolarisPuiGenerator.cs、HotReload_Click），面板里没必要再露出这个字段。
                "ShowName" => value is PuiElementType nt && nt != PuiElementType.Window ? Visibility.Visible : Visibility.Collapsed,
                "ShowSize" => IsSizeEditableElement(value) ? Visibility.Visible : Visibility.Collapsed,
                "CanDelete" => value is PuiElementType dt && dt != PuiElementType.Window ? Visibility.Visible : Visibility.Collapsed,
                "ShowWindowExtra" => Is(value, PuiElementType.Window),
                "ShowTextExtra" => Is(value, PuiElementType.Text),
                "ShowSeparatorExtra" => Is(value, PuiElementType.Separator),
                "ShowButtonExtra" => Is(value, PuiElementType.Button),
                "ShowButtonMulti" => Is(value, PuiElementType.ButtonMulti),
                "ShowChecks" => Is(value, PuiElementType.Checks),
                "ShowRadio" => Is(value, PuiElementType.Radio),
                "ShowSlider" => Is(value, PuiElementType.Slider),
                "ShowInput" => Is(value, PuiElementType.Input),
                "ShowNumCounter" => Is(value, PuiElementType.NumCounter),
                "ShowColorCell" => Is(value, PuiElementType.ColorCell),
                "ShowImage" => Is(value, PuiElementType.Image),
                "ShowCustom" => Is(value, PuiElementType.Custom),
                "EmptyListVisible" => value is IReadOnlyCollection<PuiCallbackHook> hooks && hooks.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed,
                _ => value
            };
        }

        private static Visibility Is(object value, PuiElementType type)
            => value is PuiElementType t && t == type ? Visibility.Visible : Visibility.Collapsed;

        private static bool IsTextEditableElement(object value)
        {
            return value is PuiElementType et
                && et != PuiElementType.Window
                && et != PuiElementType.LineBreak
                && et != PuiElementType.Separator
                && et != PuiElementType.ButtonMulti
                && et != PuiElementType.Checks
                && et != PuiElementType.Radio
                && et != PuiElementType.Input
                && et != PuiElementType.NumCounter
                && et != PuiElementType.Image
                && et != PuiElementType.Custom
                && !PuiElement.IsMarker(et);
        }

        private static bool IsSizeEditableElement(object value)
        {
            return value is PuiElementType st
                && st != PuiElementType.LineBreak
                && st != PuiElementType.Separator
                && !PuiElement.IsMarker(st);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class PuiLineAlignToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isDefault = value is PuiLineAlign align && align == PuiLineAlign.Left;
            return isDefault ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.Orange;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
