namespace AMD.AutoService.GaragePro.Application.Contact;

/// <summary>
/// หนึ่งข้อความใน LINE push (ข้อความหรือรูป) · ส่งได้ 1 ถึง 5 ข้อความต่อครั้ง (ข้อจำกัดของ Messaging API)
/// · รูป: <see cref="ImageUrl"/> ต้องเป็น https และเป็น JPEG/PNG ที่ LINE ดึงได้เอง (ใช้ url เดียวกันทั้งรูปเต็มและรูปย่อ)
/// </summary>
public sealed record LineOutboundMessage(string? Text, string? ImageUrl)
{
    public static LineOutboundMessage FromText(string text) => new(text, null);

    public static LineOutboundMessage FromImage(string url) => new(null, url);

    public bool IsImage => ImageUrl is not null;

    /// <summary>ข้อความแทนเมื่อปลายทางส่งได้แค่ข้อความ</summary>
    public string AsText() => Text ?? ImageUrl ?? string.Empty;

    /// <summary>fallback ร่วม: รวมทุกข้อความเป็นข้อความเดียว (รูปกลายเป็นลิงก์)</summary>
    public static string Join(IReadOnlyList<LineOutboundMessage> messages) =>
        string.Join("\n", messages.Select(m => m.AsText()).Where(t => t.Length > 0));
}
