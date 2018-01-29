using System;
using System.IO;

namespace CASCEdit.ZLib
{
    public class ZStreamWriter : Stream, IDisposable
    {
        Stream baseStream;
        zlib.z_stream zstream;

        public ZStreamWriter(Stream stream) : this(stream, zlib.Z_DEFAULT_COMPRESSION, 0x1000) { }

        public ZStreamWriter(Stream stream, int level) : this(stream, level, 0x1000) { }

        public ZStreamWriter(Stream stream, int level, uint bufferSize, int bits = zlib.MAX_WBITS)
        {
            if (!stream.CanWrite)
                throw new ArgumentException("Stream not writable");

            if (bufferSize == 0)
                throw new ArgumentOutOfRangeException("bufferSize", "must be greater zero");

            baseStream = stream;

            zstream = new zlib.z_stream();

            switch (zlib.deflateInit(zstream, level, bits))
            {
                case zlib.Z_OK:
                    break;
                case zlib.Z_MEM_ERROR:
                case zlib.Z_STREAM_ERROR:
                    throw new Exception("zlib memory error");
                default:
                    throw new Exception("Unknown zlib error");
            }

            zstream.out_buf = new byte[bufferSize];
            zstream.next_out = 0;
            zstream.avail_out = bufferSize;
        }

        public override bool CanRead { get { return false; } }

        public override bool CanSeek { get { return false; } }

        public override bool CanWrite { get { return baseStream != null && baseStream.CanWrite; } }

        public override void Flush()
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");
        }

        public override long Length
        {
            get
            {
                if (baseStream == null)
                    throw new ObjectDisposedException(null, "File closed");

                return baseStream.Length + zstream.next_out;
            }
        }

        public override long Position
        {
            get
            {
                if (baseStream == null)
                    throw new ObjectDisposedException(null, "File closed");

                return baseStream.Position + zstream.next_out;
            }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value", "must not be negative");

                if (baseStream == null)
                    throw new ObjectDisposedException(null, "File closed");

                throw new NotSupportedException("Seeking not supported");
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");

            throw new NotSupportedException("Seeking not supported");
        }

        public override void SetLength(long value)
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");

            throw new NotSupportedException("Setting the length is not supported");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");

            throw new NotSupportedException("Reading not supported");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");

            zstream.next_in = (uint)offset;
            zstream.in_buf = buffer;
            zstream.avail_in = (uint)count;

            do
            {
                int ret = zlib.deflate(zstream, zlib.Z_NO_FLUSH);
                if (ret != zlib.Z_OK)
                {
                    if (zstream.msg != null)
                        throw new Exception(zstream.msg);

                    throw new Exception("zlib error");
                }

                if (zstream.avail_out == 0)
                {
                    baseStream.Write(zstream.out_buf, 0, zstream.out_buf.Length);
                    zstream.next_out = 0;
                    zstream.avail_out = (uint)zstream.out_buf.Length;
                }
            } while (zstream.avail_in != 0);
        }

        public void WriteToEnd()
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");

            int ret;
            do
            {
                ret = zlib.deflate(zstream, zlib.Z_FINISH);
                if (ret == zlib.Z_OK)
                {
                    if (zstream.avail_out == 0)
                    {
                        baseStream.Write(zstream.out_buf, 0, zstream.next_out);
                        zstream.next_out = 0;
                        zstream.avail_out = (uint)zstream.out_buf.Length;
                    }
                }
                else if (ret != zlib.Z_STREAM_END)
                {
                    if (zstream.msg != null && zstream.msg.Length > 0)
                        throw new Exception(zstream.msg);

                    throw new Exception("zlib error");
                }
            } while (ret != zlib.Z_STREAM_END);

            if (zstream.next_out > 0)
            {
                baseStream.Write(zstream.out_buf, 0, zstream.next_out);
                zstream.next_out = 0;
                zstream.avail_out = (uint)zstream.out_buf.Length;
            }

            if (zstream != null)
                zlib.deflateEnd(zstream);
            zstream = null;
        }

#pragma warning disable CS0114
        public void Close()
        {
            try
            {
                if (zstream != null && baseStream != null)
                    WriteToEnd();
            }
            finally
            {
                if (baseStream != null)
                    baseStream.Dispose();

                baseStream = null;

                base.Dispose();
            }
        }
#pragma warning restore CS0114

        void IDisposable.Dispose()
        {
            Close();
        }
    }
}
