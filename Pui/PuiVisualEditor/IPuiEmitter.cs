using Polaris.PUI.Wire;

namespace PolarisTools.Pui.PuiVisualEditor;

/// <summary>
/// 把 <see cref="PuiTreeWalker"/> 遍历 .pui 元素树时"该发出哪个调用、参数是什么"的
/// 决策，跟"这份决策最终落到哪里"（编译期生成的 C# 源码文本 vs 运行时热重载指令）解耦。
/// 两个实现：
/// <see cref="CSharpTextEmitter"/>（编译期 codegen，落到 .g.cs 源码文本，行为与重构前逐字节一致）、
/// PolarisTools.Pui.PuiVisualEditor.HotReload.PuiHotReloadEmitter（热重载，落到发给游戏进程的 <c>PuiWireCommand</c> 序列）。
/// 新增元素类型时两边都要补一个方法实现。
/// </summary>
internal interface IPuiEmitter
{
    /// <summary>对应 GetUIWindow：source.Create(...)。</summary>
    void CreateWindow(string name, double pixelX, double pixelY, double width, double height, int appearDir, double appearLen, PuiMaskType mask);

    void SetFrameType(PuiFrameType frameType);
    void SetFocusable();

    void AddText(PuiTextParams p);
    void AddButton(PuiButtonParams p);
    void AddSeparator(PuiSeparatorParams p);
    void Br();
    void SetLineAlign(PuiLineAlign align);
    void SetDefaultLineAlign();
    void AddButtonMulti(PuiButtonMultiParams p);
    void AddChecks(PuiChecksParams p);
    void AddRadio(PuiRadioParams p);
    void AddSlider(PuiSliderParams p);
    void AddInput(PuiInputParams p);
    void AddNumCounter(PuiNumCounterParams p);
    void AddColorCell(PuiColorCellParams p);
    void AddImage(PuiImageParams p);
    void AddCustom(PuiCustomParams p);

    /// <summary>Window.OnBuildCompleted：在所有子元素语句之后触发。</summary>
    void OnBuildCompleted(string methodName);
}
