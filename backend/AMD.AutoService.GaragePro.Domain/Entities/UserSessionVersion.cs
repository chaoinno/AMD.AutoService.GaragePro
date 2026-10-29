namespace AMD.AutoService.GaragePro.Domain.Entities;

public sealed class UserSessionVersion
{
    public string ShardKey { get; set; } = "";
    public long UserId { get; set; }
    public long Version { get; set; }
    public bool Changing { get; set; }
    public DateTime UpdatedAt { get; set; }
}
