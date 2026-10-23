using System.Text.Json;
using System.Text.Json.Nodes;

namespace AMD.AutoService.GaragePro.Application.Contact;

/// <summary>
/// หนึ่งข้อความ LINE (ข้อความ · รูป · Flex) ใช้ทั้ง push และคำตอบของคำสั่ง <c>/SP_</c> · push ได้ 1 ถึง 5 ข้อความต่อครั้ง
/// · รูป: <see cref="ImageUrl"/> ต้องเป็น https และเป็น JPEG/PNG ที่ LINE ดึงได้เอง (ใช้ url เดียวกันทั้งรูปเต็มและรูปย่อ)
/// · Flex: <see cref="AltText"/> (ข้อความแทนบนแจ้งเตือน/รายการแชท) + <see cref="FlexContents"/> (bubble หรือ carousel)
/// </summary>
public sealed record LineOutboundMessage(string? Text, string? ImageUrl, string? AltText = null, JsonObject? FlexContents = null)
{
    /// <summary>ข้อจำกัดของ LINE: altText ≤ 1500 ตัว · JSON ของ bubble ≤ 30 KB (carousel ≤ 50 KB)</summary>
    public const int MaxAltTextLength = 1500;
    public const int MaxBubbleJsonBytes = 30 * 1024;
    public const int MaxCarouselJsonBytes = 50 * 1024;

    public static LineOutboundMessage FromText(string text) => new(text, null);

    public static LineOutboundMessage FromImage(string url) => new(null, url);

    public static LineOutboundMessage FromFlex(string altText, JsonObject contents) => new(null, null, altText, contents);

    public bool IsFlex => FlexContents is not null;

    public bool IsImage => ImageUrl is not null && !IsFlex;

    /// <summary>ข้อความแทนเมื่อปลายทางส่งได้แค่ข้อความ</summary>
    public string AsText() => Text ?? AltText ?? ImageUrl ?? string.Empty;

    /// <summary>fallback ร่วม: รวมทุกข้อความเป็นข้อความเดียว (รูปกลายเป็นลิงก์ · Flex กลายเป็น altText)</summary>
    public static string Join(IReadOnlyList<LineOutboundMessage> messages) =>
        string.Join("\n", messages.Select(m => m.AsText()).Where(t => t.Length > 0));

    /// <summary>
    /// message object ของ Messaging API: <c>{type:"text",text}</c> · <c>{type:"image",originalContentUrl,previewImageUrl}</c> ·
    /// <c>{type:"flex",altText,contents}</c> · ผิดเงื่อนไขของ LINE = <see cref="ArgumentException"/> ก่อนยิง
    /// (ดีกว่าโดน 400 แล้วข้อความทั้งชุดหาย)
    /// </summary>
    public JsonObject ToLineJson()
    {
        if (IsFlex)
        {
            if (string.IsNullOrWhiteSpace(AltText) || AltText.Length > MaxAltTextLength)
                throw new ArgumentException($"altText ของ Flex ต้องมี 1 ถึง {MaxAltTextLength} ตัวอักษร");
            var type = FlexContents!["type"]?.GetValue<string>();
            if (type is not ("bubble" or "carousel"))
                throw new ArgumentException("contents ของ Flex ต้องเป็น bubble หรือ carousel");
            var size = JsonSerializer.SerializeToUtf8Bytes(FlexContents).Length;
            if (size > (type == "bubble" ? MaxBubbleJsonBytes : MaxCarouselJsonBytes))
                throw new ArgumentException("Flex ใหญ่เกินที่ LINE รับได้");
            return new JsonObject { ["type"] = "flex", ["altText"] = AltText, ["contents"] = FlexContents.DeepClone() };
        }

        if (IsImage)
        {
            if (!Uri.TryCreate(ImageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("รูปใน LINE ต้องเป็นลิงก์ https");
            return new JsonObject { ["type"] = "image", ["originalContentUrl"] = ImageUrl, ["previewImageUrl"] = ImageUrl };
        }

        return new JsonObject { ["type"] = "text", ["text"] = Text ?? string.Empty };
    }
}
