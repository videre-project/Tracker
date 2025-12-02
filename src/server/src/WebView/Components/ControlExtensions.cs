using System.Windows.Forms;
using System.Reflection;

namespace Tracker.WebView.Components;

internal static class ControlExtensions
{
  public static void SetDoubleBuffered(this Control control, bool enable)
  {
    var prop = control.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
    if (prop != null)
        prop.SetValue(control, enable, null);
  }
}