using System.IO;

namespace fast_export;

public class DataCommand : Command
{
    internal byte[] _Bytes;

    public DataCommand(byte[] bytes)
    {
        this._Bytes = (byte[])bytes.Clone();
    }

    public DataCommand(string str)
    {
        this._Bytes = Command.StreamEncoding.GetBytes(str);
    }

    public override void RenderCommand(Stream stream)
    {
        stream.WriteLine($"data {_Bytes.Length}");
        stream.Write(_Bytes, 0, _Bytes.Length);
        stream.WriteLineFeed();
    }
}
