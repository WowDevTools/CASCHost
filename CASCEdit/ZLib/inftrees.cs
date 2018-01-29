// inftrees.cs -- generate Huffman trees for efficient decoding
// Copyright (C) 1995-2010 Mark Adler
// Copyright (C) 2007-2011 by the Authors
// For conditions of distribution and use, see copyright notice in License.txt
using System;

namespace CASCEdit.ZLib
{
    public static partial class zlib
    {
        // Structure for decoding tables.  Each entry provides either the
        // information needed to do the operation requested by the code that
        // indexed that table entry, or it provides a pointer to another
        // table that indexes more bits of the code.  op indicates whether
        // the entry is a pointer to another table, a literal, a length or
        // distance, an end-of-block, or an invalid code.  For a table
        // pointer, the low four bits of op is the number of index bits of
        // that table.  For a length or distance, the low four bits of op
        // is the number of extra bits to get after the code.  bits is
        // the number of bits in this code or part of the code to drop off
        // of the bit buffer.  val is the actual byte to output in the case
        // of a literal, the base length or distance, or the offset from
        // the current table to the next table. Each entry is four bytes.
        private class code
        {
            public byte op;     // operation, extra bits, table bits
            public byte bits;   // bits in this part of the code
            public ushort val;  // offset in table or code value

            public code(byte op, byte bits, ushort val)
            {
                this.op = op;
                this.bits = bits;
                this.val = val;
            }

            public code Clone()
            {
                return new code(op, bits, val);
            }
        }

        // op values as set by inflate_table():
        //	00000000 - literal
        //	0000tttt - table link, tttt != 0 is the number of table index bits
        //	0001eeee - length or distance, eeee is the number of extra bits
        //	01100000 - end of block
        //	01000000 - invalid code

        // Maximum size of the dynamic table. The maximum number of code structures is
        // 1444, which is the sum of 852 for literal/length codes and 592 for distance
        // codes. These values were found by exhaustive searches using the program
        // examples/enough.c found in the zlib distribtution. The arguments to that
        // program are the number of symbols, the initial root table size, and the
        // maximum bit length of a code. "enough 286 9 15" for literal/length codes
        // returns returns 852, and "enough 30 6 15" for distance codes returns 592.
        // The initial root table size (9 or 6) is found in the fifth argument of the
        // inflate_table() calls in inflate.c and infback.c. If the root table size is
        // changed, then these maximum sizes would be need to be recalculated and
        // updated.
        private const int ENOUGH_LENS = 852;
        private const int ENOUGH_DISTS = 592;
        private const int ENOUGH = (ENOUGH_LENS + ENOUGH_DISTS);

        // Type of code to build for inflate_table()
        private enum codetype
        {
            CODES,
            LENS,
            DISTS
        }

        private const int MAXBITS = 15;

        // If you use the zlib library in a product, an acknowledgment is welcome
        // in the documentation of your product. If for some reason you cannot
        // include such an acknowledgment, I would appreciate that you keep this
        // copyright string in the executable of your product.
        private const string inflate_copyright = " inflate 1.2.5 Copyright 1995-2010 Mark Adler ";

        // Build a set of tables to decode the provided canonical Huffman code.
        // The code lengths are lens[0..codes-1].  The result starts at *table,
        // whose indices are 0..2^bits-1.  work is a writable array of at least
        // lens shorts, which is used as a work area.  type is the type of code
        // to be generated, CODES, LENS, or DISTS.  On return, zero is success,
        // -1 is an invalid code, and +1 means that ENOUGH isn't enough.  table
        // on return points to the next available entry's address.  bits is the
        // requested root table index bits, and on return it is the actual root
        // table index bits.  It will differ if the request is greater than the
        // longest code or if it is less than the shortest code.

        // Length codes 257..285 base
        private static readonly ushort[] lbase = new ushort[31]
        {
            3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
            35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258, 0, 0
        };

        // Length codes 257..285 extra
        private static readonly ushort[] lext = new ushort[31]
        {
            16, 16, 16, 16, 16, 16, 16, 16, 17, 17, 17, 17, 18, 18, 18, 18,
            19, 19, 19, 19, 20, 20, 20, 20, 21, 21, 21, 21, 16, 73, 195
        };

        // Distance codes 0..29 base
        private static readonly ushort[] dbase = new ushort[32]
        {
            1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
            257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
            8193, 12289, 16385, 24577, 0, 0
        };

        // Distance codes 0..29 extra
        private static readonly ushort[] dext = new ushort[32]
        {
            16, 16, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22,
            23, 23, 24, 24, 25, 25, 26, 26, 27, 27, 28, 28, 29, 29, 64, 64
        };

        // ushort* lens -> ushort[] lens + int lens_ind
        // code** table -> code[] table + ref int table_ind
        private static int inflate_table(codetype type, ushort[] lens, int lens_ind, uint codes, code[] table, ref int table_ind, ref uint bits, ushort[] work)
        {
            uint len;               // a code's length in bits
            uint sym;               // index of code symbols
            uint min, max;          // minimum and maximum code lengths
            uint root;              // number of index bits for root table
            uint curr;              // number of index bits for current table
            uint drop;              // code bits to drop for sub-table
            int left;               // number of prefix codes available
            uint used;              // code entries in table used
            uint huff;              // Huffman code
            uint incr;              // for incrementing code, index
            uint fill;              // index for replicating entries
            uint low;               // low bits for current root entry
            uint mask;              // mask for low root bits
            code here;              // table entry for duplication
            int next;               // next available space in table
            ushort[] _base;         // base value table to use
            int base_ind;           // index in _base
            ushort[] extra;         // extra bits table to use
            int extra_ind;          // index in extra
            int end;                // use base and extra for symbol > end
            ushort[] count = new ushort[MAXBITS + 1];   // number of codes of each length
            ushort[] offs = new ushort[MAXBITS + 1];    // offsets in table for each length

            // Process a set of code lengths to create a canonical Huffman code.  The
            // code lengths are lens[0..codes-1].  Each length corresponds to the
            // symbols 0..codes-1.  The Huffman code is generated by first sorting the
            // symbols by length from short to long, and retaining the symbol order
            // for codes with equal lengths.  Then the code starts with all zero bits
            // for the first code of the shortest length, and the codes are integer
            // increments for the same length, and zeros are appended as the length
            // increases.  For the deflate format, these bits are stored backwards
            // from their more natural integer increment ordering, and so when the
            // decoding tables are built in the large loop below, the integer codes
            // are incremented backwards.

            // This routine assumes, but does not check, that all of the entries in
            // lens[] are in the range 0..MAXBITS.  The caller must assure this.
            // 1..MAXBITS is interpreted as that code length.  zero means that that
            // symbol does not occur in this code.

            // The codes are sorted by computing a count of codes for each length,
            // creating from that a table of starting indices for each length in the
            // sorted table, and then entering the symbols in order in the sorted
            // table.  The sorted table is work[], with that space being provided by
            // the caller.

            // The length counts are used for other purposes as well, i.e. finding
            // the minimum and maximum length codes, determining if there are any
            // codes at all, checking for a valid set of lengths, and looking ahead
            // at length counts to determine sub-table sizes when building the
            // decoding tables.

            // accumulate lengths for codes (assumes lens[] all in 0..MAXBITS)
            for (len = 0; len <= MAXBITS; len++) count[len] = 0;
            for (sym = 0; sym < codes; sym++) count[lens[lens_ind + sym]]++;

            // bound code lengths, force root to be within code lengths
            root = bits;
            for (max = MAXBITS; max >= 1; max--) if (count[max] != 0) break;
            if (root > max) root = max;

            if (max == 0)
            {                               // no symbols to code at all
                here = new code(64, 1, 0);  // invalid code marker
                table[table_ind++] = here.Clone();  // make a table to force an error
                table[table_ind++] = here.Clone();
                bits = 1;
                return 0;                   // no symbols, but wait for decoding to report error
            }

            for (min = 1; min < max; min++) if (count[min] != 0) break;
            if (root < min) root = min;

            // check for an over-subscribed or incomplete set of lengths
            left = 1;
            for (len = 1; len <= MAXBITS; len++)
            {
                left <<= 1;
                left -= count[len];
                if (left < 0) return -1; // over-subscribed
            }
            if (left > 0 && (type == codetype.CODES || max != 1)) return -1; // incomplete set

            // generate offsets into symbol table for each length for sorting
            offs[1] = 0;
            for (len = 1; len < MAXBITS; len++) offs[len + 1] = (ushort)(offs[len] + count[len]);

            // sort symbols by length, by symbol order within each length
            for (sym = 0; sym < codes; sym++)
                if (lens[lens_ind + sym] != 0) work[offs[lens[lens_ind + sym]]++] = (ushort)sym;

            // Create and fill in decoding tables. In this loop, the table being
            // filled is at next and has curr index bits. The code being used is huff
            // with length len. That code is converted to an index by dropping drop
            // bits off of the bottom. For codes where len is less than drop + curr,
            // those top drop + curr - len bits are incremented through all values to
            // fill the table with replicated entries.

            // root is the number of index bits for the root table. When len exceeds
            // root, sub-tables are created pointed to by the root entry with an index
            // of the low root bits of huff. This is saved in low to check for when a
            // new sub-table should be started. drop is zero when the root table is
            // being filled, and drop is root when sub-tables are being filled.

            // When a new sub-table is needed, it is necessary to look ahead in the
            // code lengths to determine what size sub-table is needed.  The length
            // counts are used for this, and so count[] is decremented as codes are
            // entered in the tables.

            // used keeps track of how many table entries have been allocated from the
            // provided *table space.  It is checked for LENS and DIST tables against
            // the constants ENOUGH_LENS and ENOUGH_DISTS to guard against changes in
            // the initial root table size constants. See the comments in inftrees.h
            // for more information.

            // sym increments through all symbols, and the loop terminates when
            // all codes of length max, i.e. all codes, have been processed. This
            // routine permits incomplete codes, so another loop after this one fills
            // in the rest of the decoding tables with invalid code markers.

            // set up for code type
            switch (type)
            {
                case codetype.CODES:
                    base_ind = extra_ind = 0;
                    _base = extra = work;   // dummy value--not used
                    end = 19;
                    break;
                case codetype.LENS:
                    _base = lbase;
                    base_ind = -257;
                    extra = lext;
                    extra_ind = -257;
                    end = 256;
                    break;
                default:                // DISTS
                    base_ind = extra_ind = 0;
                    _base = dbase;
                    extra = dext;
                    end = -1;
                    break;
            }

            // initialize state for loop
            huff = 0;               // starting code
            sym = 0;                // starting code symbol
            len = min;          // starting code length
            next = table_ind;       // current table to fill in
            curr = root;            // current table index bits
            drop = 0;               // current bits to drop from code for index
            low = uint.MaxValue;    // trigger new sub-table when len > root
            used = 1U << (int)root; // use root table entries
            mask = used - 1;        // mask for comparing low

            // check available table space
            if ((type == codetype.LENS && used >= ENOUGH_LENS) || (type == codetype.DISTS && used >= ENOUGH_DISTS)) return 1;

            // process all codes and make table entries
            for (;;)
            {
                // create table entry
                here = new code(0, (byte)(len - drop), 0);
                if ((int)(work[sym]) < end)
                {
                    here.op = 0;
                    here.val = work[sym];
                }
                else if ((int)(work[sym]) > end)
                {
                    here.op = (byte)(extra[extra_ind + work[sym]]);
                    here.val = _base[base_ind + work[sym]];
                }
                else
                {
                    here.op = 32 + 64;      // end of block
                    here.val = 0;
                }

                // replicate for those indices with low len bits equal to huff
                incr = 1U << (int)(len - drop);
                fill = 1U << (int)curr;
                min = fill;             // save offset to next table
                do
                {
                    fill -= incr;
                    table[next + (huff >> (int)drop) + fill] = here.Clone();
                } while (fill != 0);

                // backwards increment the len-bit code huff
                incr = 1U << (int)(len - 1);
                while ((huff & incr) != 0) incr >>= 1;

                if (incr != 0)
                {
                    huff &= incr - 1;
                    huff += incr;
                }
                else huff = 0;

                // go to next symbol, update count, len
                sym++;
                if (--(count[len]) == 0)
                {
                    if (len == max) break;
                    len = lens[lens_ind + work[sym]];
                }

                // create new sub-table if needed
                if (len > root && (huff & mask) != low)
                {
                    // if first time, transition to sub-tables
                    if (drop == 0) drop = root;

                    // increment past last table
                    next += (int)min;           // here min is 1 << curr

                    // determine length of next table
                    curr = len - drop;
                    left = 1 << (int)curr;
                    while (curr + drop < max)
                    {
                        left -= count[curr + drop];
                        if (left <= 0) break;
                        curr++;
                        left <<= 1;
                    }

                    // check for enough space
                    used += 1U << (int)curr;
                    if ((type == codetype.LENS && used >= ENOUGH_LENS) || (type == codetype.DISTS && used >= ENOUGH_DISTS)) return 1;

                    // point entry in root table to sub-table
                    low = huff & mask;
                    table[table_ind + low] = new code((byte)curr, (byte)root, (ushort)(next - table_ind));
                }
            }

            // Fill in rest of table for incomplete codes.  This loop is similar to the
            // loop above in incrementing huff for table indices.  It is assumed that
            // len is equal to curr + drop, so there is no loop needed to increment
            // through high index bits.  When the current sub-table is filled, the loop
            // drops back to the root table to fill in any remaining entries there.

            here = new code(64, (byte)(len - drop), 0); // invalid code marker
            while (huff != 0)
            {
                // when done with sub-table, drop back to root table
                if (drop != 0 && (huff & mask) != low)
                {
                    drop = 0;
                    len = root;
                    next = table_ind;
                    here.bits = (byte)len;
                }

                // put invalid code marker in table
                table[next + huff >> (int)drop] = here.Clone();

                // backwards increment the len-bit code huff
                incr = 1U << (int)(len - 1);
                while ((huff & incr) != 0) incr >>= 1;
                if (incr != 0)
                {
                    huff &= incr - 1;
                    huff += incr;
                }
                else huff = 0;
            }

            // set return parameters
            table_ind += (int)used;
            bits = root;
            return 0;
        }
    }
}
