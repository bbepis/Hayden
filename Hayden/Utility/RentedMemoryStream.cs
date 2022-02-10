using System;
using System.Buffers;
using System.IO;

namespace Hayden
{
	public class RentedMemoryStream : Stream
	{
		public IMemoryOwner<byte> RentedMemory { get; }

		public int Capacity => RentedMemory.Memory.Length;

		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => true;

		protected long _length = 0;
		public override long Length => _length;
		public override long Position { get; set; } = 0;

		public RentedMemoryStream(int capacity) : this(MemoryPool<byte>.Shared.Rent(capacity)) { }

		public RentedMemoryStream(IMemoryOwner<byte> rentedMemory)
		{
			RentedMemory = rentedMemory;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			return Read(buffer.AsSpan(offset, count));
		}

		public override int Read(Span<byte> buffer)
		{
			int maxRead = (int)Math.Min(buffer.Length, Length - Position);

			if (maxRead <= 0)
				return 0;

			RentedMemory.Memory.Span.Slice((int)Position, maxRead).CopyTo(buffer);

			Position += maxRead;

			return maxRead;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			Position = origin switch
			{
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => Position + offset,
				SeekOrigin.End => Length + offset,
				_ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
			};

			return Position;
		}

		public override void SetLength(long value)
		{
			throw new InvalidOperationException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			Write(buffer.AsSpan(offset, count));
		}

		public override void Write(ReadOnlySpan<byte> buffer)
		{
			int maxWrite = (int)Math.Min(buffer.Length, Capacity - Position);

			if (maxWrite < buffer.Length)
				throw new InvalidOperationException("Write would exceed stream capacity");


			buffer.CopyTo(RentedMemory.Memory.Span.Slice((int)Position));

			Position += maxWrite;

			if (Length < Position)
				_length = Position;
		}

		public byte[] ToArray()
		{
			var buffer = new byte[Length];

			RentedMemory.Memory.Slice(0, (int)Length).CopyTo(buffer);

			return buffer;
		}

		public override void Flush() { }

		protected override void Dispose(bool disposing)
		{
			RentedMemory.Dispose();
		}
	}
}
