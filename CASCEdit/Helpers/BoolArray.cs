using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CASCEdit.Helpers
{
	public class BoolArray : ICollection<bool>
	{
		private List<bool> values = new List<bool>();
		

		public BoolArray() { }

		public BoolArray(byte[] bytes)
		{
			for (int i = 0; i < bytes.Length; i++)
				bytes[i] = (byte)((bytes[i] * 0x0202020202 & 0x010884422010) % 1023); // reverse each byte

			var ba = new BitArray(bytes);
			for (int i = 0; i < ba.Length; i++)
				values.Add(ba[i]);
		}


		public void Insert(int i, bool v) => values.Insert(i, v);

		public bool Set(int i) => values[i] = true;

		public bool UnSet(int i) => values[i] = false;

		public bool Get(int i) => values[i];

		public void Add(bool v) => values.Add(v);

		public void Clear() => values.Clear();

		public void RemoveAt(int index) => values.RemoveAt(index);

		public int Count => values.Count;

		public bool IsReadOnly => false;

		public bool Contains(bool item) => throw new NotImplementedException();

		public void CopyTo(bool[] array, int arrayIndex) => throw new NotImplementedException();

		public bool Remove(bool item) => throw new NotImplementedException();

		public IEnumerator<bool> GetEnumerator() => values.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => values.GetEnumerator();

		public byte[] ToByteArray()
		{
			var ba = new BitArray(values.ToArray());
			byte[] bytes = new byte[(ba.Length + 7) / 8];
			ba.CopyTo(bytes, 0);

			for (int i = 0; i < bytes.Length; i++)
				bytes[i] = (byte)((bytes[i] * 0x0202020202 & 0x010884422010) % 1023); // reverse each byte

			return bytes;
		}
	}
}
