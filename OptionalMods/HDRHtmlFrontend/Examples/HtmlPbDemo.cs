// Whole PB script. Put PlainHtmlPbDemo.html in this PB's CustomData.
// Name an authorized LCD, Console or Projector "HTML Display".
// The frontend mod owns mounting, refresh, data binding and cleanup.
public void Main(string argument)
{
    var property = Me.GetProperty("HDR.Html");
    if (property == null) { Echo("Requires the HDR HTML frontend world mod."); return; }
    try
    {
        var html = property.As<Func<string, object[], object>>().GetValue(Me);
        if (html == null) { Echo("HDR HTML is unavailable."); return; }
        Echo((string)html("run", new object[] { "HTML Display", argument ?? "" }));
    }
    catch (Exception error) { Echo(error.Message); }
}
