
using I2PCore.Utils;

public class DatabaseLookupKeyInfo
{
    public bool EncryptionFlag { get; set; }
    public bool ECIESFlag { get; set; }
    public BufLen ReplyKey { get; set; }
    public BufLen[] Tags { get; set; }
}