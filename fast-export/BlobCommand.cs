using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace fast_export;

public class BlobCommand : MarkCommand
{
    private static Dictionary<string, BlobCommand> _DataBlobs = new();

    public static BlobCommand BuildBlob(byte[] data, int? markId)
    {
        var hash = Convert.ToHexString(SHA1.HashData(data));
        if (_DataBlobs.TryGetValue(hash, out var existing))
        {
            if (existing.DataCommand._Bytes.Length != data.Length)
                throw new InvalidOperationException("Two matching SHA-1 hashes with different data lengths.");
            return existing;
        }

        var blob = new BlobCommand(data, markId);
        _DataBlobs[hash] = blob;
        return blob;
    }

    public bool IsRendered { get; set; }
    public DataCommand DataCommand { get; private set; }

    private BlobCommand(DataCommand data, int? markId)
    {
        this.DataCommand = data;
        this.MarkId = markId;
        this.IsRendered = false;
    }

    private BlobCommand(byte[] data, int? markId)
        : this(new DataCommand(data), markId) { }

    public override void RenderCommand(Stream stream)
    {
        if (!IsRendered)
        {
            IsRendered = true;
            stream.WriteLine("blob");
            base.RenderMarkCommand(stream);
            stream.RenderCommand(DataCommand);
        }
    }
}
