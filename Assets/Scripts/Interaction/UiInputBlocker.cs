using UnityEngine;

/// <summary>
/// 有独占键盘输入的界面（比如密码箱键盘）打开时置为 true，
/// 让角色移动、背包、剧情翻页暂时让路，避免同一个按键被多处响应。
/// </summary>
public static class UiInputBlocker
{
    public static bool Active;
}
