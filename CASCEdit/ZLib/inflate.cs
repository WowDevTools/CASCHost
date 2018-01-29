// inflate.cs -- internal inflate state definition & zlib decompression
// Copyright (C) 1995-2009 Mark Adler
// Copyright (C) 2007-2011 by the Authors
// For conditions of distribution and use, see copyright notice in License.txt

using System;

namespace CASCEdit.ZLib
{
    public static partial class zlib
    {
        // Possible inflate modes between inflate() calls
        enum inflate_mode
        {
            HEAD,       // i: waiting for magic header
            FLAGS,      // i: waiting for method and flags (gzip)
            TIME,       // i: waiting for modification time (gzip)
            OS,         // i: waiting for extra flags and operating system (gzip)
            EXLEN,      // i: waiting for extra length (gzip)
            EXTRA,      // i: waiting for extra bytes (gzip)
            NAME,       // i: waiting for end of file name (gzip)
            COMMENT,    // i: waiting for end of comment (gzip)
            HCRC,       // i: waiting for header crc (gzip)
            DICTID,     // i: waiting for dictionary check value
            DICT,       // waiting for inflateSetDictionary() call
            TYPE,       // i: waiting for type bits, including last-flag bit
            TYPEDO,     // i: same, but skip check to exit inflate on new block
            STORED,     // i: waiting for stored size (length and complement)
            COPY_,      // i/o: same as COPY below, but only first time in
            COPY,       // i/o: waiting for input or output to copy stored block
            TABLE,      // i: waiting for dynamic block table lengths
            LENLENS,    // i: waiting for code length code lengths
            CODELENS,   // i: waiting for length/lit and distance code lengths
            LEN_,       // i: same as LEN below, but only first time in
            LEN,        // i: waiting for length/lit/eob code
            LENEXT,     // i: waiting for length extra bits
            DIST,       // i: waiting for distance code
            DISTEXT,    // i: waiting for distance extra bits
            MATCH,      // o: waiting for output space to copy string
            LIT,        // o: waiting for output space to write literal
            CHECK,      // i: waiting for 32-bit check value
            LENGTH,     // i: waiting for 32-bit length (gzip)
            DONE,       // finished check, done -- remain here until reset
            BAD,        // got a data error -- remain here until reset
            MEM,        // got an inflate() memory error -- remain here until reset
            SYNC        // looking for synchronization bytes to restart inflate()
        };

        // State transitions between above modes -
        //
        // (most modes can go to BAD or MEM on error -- not shown for clarity)
        //
        // Process header:
        //    HEAD -> (gzip) or (zlib) or (raw)
        //    (gzip) -> FLAGS -> TIME -> OS -> EXLEN -> EXTRA -> NAME -> COMMENT ->
        //              HCRC -> TYPE
        //    (zlib) -> DICTID or TYPE
        //    DICTID -> DICT -> TYPE
        //    (raw) -> TYPEDO
        // Read deflate blocks:
        //        TYPE -> TYPEDO -> STORED or TABLE or LEN_ or CHECK
        //        STORED -> COPY_ -> COPY -> TYPE
        //        TABLE -> LENLENS -> CODELENS -> LEN_
        //        LEN_ -> LEN
        // Read deflate codes in fixed or dynamic block:
        //            LEN -> LENEXT or LIT or TYPE
        //            LENEXT -> DIST -> DISTEXT -> MATCH -> LEN
        //            LIT -> LEN
        // Process trailer:
        //    CHECK -> LENGTH -> DONE

        // state maintained between inflate() calls. Approximately 10K bytes.
        class inflate_state
        {
            public inflate_mode mode;   // current inflate mode
            public int last;            // true if processing last block
            public int wrap;            // bit 0 true for zlib, bit 1 true for gzip
            public int havedict;        // true if dictionary provided
            public int flags;           // gzip header method and flags (0 if zlib)
            public uint dmax;           // zlib header max distance
            public uint check;          // protected copy of check value
            public uint total;          // protected copy of output count
            public gz_header head;      // where to save gzip header information

            // sliding window
            public uint wbits;          // log base 2 of requested window size
            public uint wsize;          // window size or zero if not using window
            public uint whave;          // valid bytes in the window
            public uint wnext;          // window write index
            public byte[] window;       // allocated sliding window, if needed

            // bit accumulator
            public uint hold;           // input bit accumulator
            public uint bits;           // number of bits in "in"

            // for string and stored block copying
            public uint length;     // literal or length of data to copy
            public uint offset;     // distance back to copy string from

            // for table and code decoding
            public uint extra;      // extra bits needed

            // fixed and dynamic code tables
            public code[] lencode;  // starting table for length/literal codes
            public code[] distcode; // starting table for distance codes
            public int distcode_ind;// index of the start in distcode
            public uint lenbits;    // index bits for lencode
            public uint distbits;   // index bits for distcode

            // dynamic table building
            public uint ncode;      // number of code length code lengths
            public uint nlen;       // number of length code lengths
            public uint ndist;      // number of distance code lengths
            public uint have;       // number of code lengths in lens[]
            public int next;        // next available space in codes[]
            public ushort[] lens = new ushort[320]; // temporary storage for code lengths
            public ushort[] work = new ushort[288]; // work area for code table building
            public code[] codes = new code[ENOUGH]; // space for code tables
            public bool sane;       // if false, allow invalid distance too far
            public int back;        // bits back of last unprocessed length/lit
            public uint was;        // initial length of match

            public inflate_state GetCopy()
            {
                inflate_state ret = (inflate_state)MemberwiseClone();

                ret.lens = new ushort[320];
                lens.CopyTo(ret.lens, 0);

                ret.work = new ushort[288];
                work.CopyTo(ret.work, 0);

                ret.codes = new code[codes.Length];
                for (int i = 0; i < codes.Length; i++) ret.codes[i] = codes[i].Clone();

                if (lencode == codes) ret.lencode = ret.distcode = ret.codes;

                if (window != null)
                {
                    uint wsize = 1U << (int)wbits;
                    ret.window = new byte[wsize];
                    Buffer.BlockCopy(window, 0, ret.window, 0, (int)wsize);
                }

                return ret;
            }
        };

        //   This function is equivalent to inflateEnd followed by inflateInit,
        // but does not free and reallocate all the internal decompression state.
        // The stream will keep attributes that may have been set by inflateInit2.

        //    inflateReset returns Z_OK if success, or Z_STREAM_ERROR if the source
        // stream state was inconsistent (such as zalloc or state being NULL).

        public static int inflateReset(z_stream strm)
        {
            if (strm == null || strm.state == null) return Z_STREAM_ERROR;

            inflate_state state = (inflate_state)strm.state;
            strm.total_in = strm.total_out = state.total = 0;
            strm.msg = null;
            strm.adler = 1;        // to support ill-conceived Java test suite
            state.mode = inflate_mode.HEAD;
            state.last = 0;
            state.havedict = 0;
            state.dmax = 32768;
            state.head = null;

            state.wsize = 0;
            state.whave = 0;
            state.wnext = 0;
            state.hold = 0;
            state.bits = 0;
            state.lencode = state.distcode = state.codes;
            state.distcode_ind = 0;
            state.next = 0;
            state.sane = true;
            state.back = -1;
            //Tracev((stderr, "inflate: reset\n"));
            return Z_OK;
        }

        public static int inflateReset2(z_stream strm, int windowBits)
        {
            // get the state
            if (strm == null || strm.state == null) return Z_STREAM_ERROR;
            inflate_state state = (inflate_state)strm.state;

            int wrap;

            // extract wrap request from windowBits parameter
            if (windowBits < 0)
            {
                wrap = 0;
                windowBits = -windowBits;
            }
            else
            {
                wrap = (windowBits >> 4) + 1;
                if (windowBits < 48) windowBits &= 15;
            }

            // set number of window bits, free window if different 
            if (windowBits != 0 && (windowBits < 8 || windowBits > 15)) return Z_STREAM_ERROR;
            if (state.window != null && state.wbits != (uint)windowBits)
                state.window = null;

            // update state and reset the rest of it
            state.wrap = wrap;
            state.wbits = (uint)windowBits;
            return inflateReset(strm);
        }

        //   This is another version of inflateInit with an extra parameter. The
        // fields next_in, avail_in, zalloc, zfree and opaque must be initialized
        // before by the caller.

        //   The windowBits parameter is the base two logarithm of the maximum window
        // size (the size of the history buffer).  It should be in the range 8..15 for
        // this version of the library. The default value is 15 if inflateInit is used
        // instead. windowBits must be greater than or equal to the windowBits value
        // provided to deflateInit2() while compressing, or it must be equal to 15 if
        // deflateInit2() was not used. If a compressed stream with a larger window
        // size is given as input, inflate() will return with the error code
        // Z_DATA_ERROR instead of trying to allocate a larger window.

        //   windowBits can also be -8..-15 for raw inflate. In this case, -windowBits
        // determines the window size. inflate() will then process raw deflate data,
        // not looking for a zlib or gzip header, not generating a check value, and not
        // looking for any check values for comparison at the end of the stream. This
        // is for use with other formats that use the deflate compressed data format
        // such as zip.  Those formats provide their own check values. If a custom
        // format is developed using the raw deflate format for compressed data, it is
        // recommended that a check value such as an adler32 or a crc32 be applied to
        // the uncompressed data as is done in the zlib, gzip, and zip formats.  For
        // most applications, the zlib format should be used as is. Note that comments
        // above on the use in deflateInit2() applies to the magnitude of windowBits.

        //   windowBits can also be greater than 15 for optional gzip decoding. Add
        // 32 to windowBits to enable zlib and gzip decoding with automatic header
        // detection, or add 16 to decode only the gzip format (the zlib format will
        // return a Z_DATA_ERROR).  If a gzip stream is being decoded, strm.adler is
        // a crc32 instead of an adler32.

        //   inflateInit2 returns Z_OK if success, Z_MEM_ERROR if there was not enough
        // memory, Z_STREAM_ERROR if a parameter is invalid (such as a null strm). msg
        // is set to null if there is no error message.  inflateInit2 does not perform
        // any decompression apart from reading the zlib header if present: this will
        // be done by inflate(). (So next_in and avail_in may be modified, but next_out
        // and avail_out are unchanged.)

        public static int inflateInit2(z_stream strm, int windowBits)
        {
            if (strm == null) return Z_STREAM_ERROR;
            strm.msg = null;                 // in case we return an error

            inflate_state state;
            try
            {
                state = new inflate_state();
            }
            catch (Exception)
            {
                return Z_MEM_ERROR;
            }

            //Tracev((stderr, "inflate: allocated\n"));
            strm.state = state;
            state.window = null;
            int ret = inflateReset2(strm, windowBits);
            if (ret != Z_OK) strm.state = null;

            return ret;
        }

        //   Initializes the internal stream state for decompression. The fields
        // next_in, avail_in, zalloc, zfree and opaque must be initialized before by
        // the caller. If next_in is not Z_NULL and avail_in is large enough (the exact
        // value depends on the compression method), inflateInit determines the
        // compression method from the zlib header and allocates all data structures
        // accordingly; otherwise the allocation will be deferred to the first call of
        // inflate.  If zalloc and zfree are set to Z_NULL, inflateInit updates them to
        // use default allocation functions.

        //   inflateInit returns Z_OK if success, Z_MEM_ERROR if there was not enough
        // memory, Z_VERSION_ERROR if the zlib library version is incompatible with the
        // version assumed by the caller.  msg is set to null if there is no error
        // message. inflateInit does not perform any decompression apart from reading
        // the zlib header if present: this will be done by inflate().  (So next_in and
        // avail_in may be modified, but next_out and avail_out are unchanged.)
        public static int inflateInit(z_stream strm)
        {
            return inflateInit2(strm, DEF_WBITS);
        }

        //    This function inserts bits in the inflate input stream.  The intent is
        // that this function is used to start inflating at a bit position in the
        // middle of a byte.  The provided bits will be used before any bytes are used
        // from next_in.  This function should only be used with raw inflate, and
        // should be used before the first inflate() call after inflateInit2() or
        // inflateReset().  bits must be less than or equal to 16, and that many of the
        // least significant bits of value will be inserted in the input.

        //    inflatePrime returns Z_OK if success, or Z_STREAM_ERROR if the source
        // stream state was inconsistent.

        public static int inflatePrime(z_stream strm, int bits, int value)
        {
            if (strm == null || strm.state == null) return Z_STREAM_ERROR;
            inflate_state state = (inflate_state)strm.state;
            if (bits < 0)
            {
                state.hold = 0;
                state.bits = 0;
                return Z_OK;
            }
            if (bits > 16 || state.bits + bits > 32) return Z_STREAM_ERROR;
            value &= (1 << (int)bits) - 1;
            state.hold += (uint)(value << (int)state.bits);
            state.bits += (uint)bits;
            return Z_OK;
        }

        // Return state with length and distance decoding tables and index sizes set to
        // fixed code decoding.
        static void fixedtables(inflate_state state)
        {
            state.lencode = lenfix;
            state.lenbits = 9;
            state.distcode = distfix;
            state.distcode_ind = 0;
            state.distbits = 5;
        }

        // Update the window with the last wsize (normally 32K) bytes written before
        // returning.  If window does not exist yet, create it.  This is only called
        // when a window is already in use, or when output has been written during this
        // inflate call, but the end of the deflate stream has not been reached yet.
        // It is also called to create a window for dictionary data when a dictionary
        // is loaded.
        //
        // Providing output buffers larger than 32K to inflate() should provide a speed
        // advantage, since only the last 32K of output is copied to the sliding window
        // upon return from inflate(), and since all distances after the first 32K of
        // output will fall in the output data, making match copies simpler and faster.
        // The advantage may be dependent on the size of the processor's data caches.
        static int updatewindow(z_stream strm, uint _out)
        {
            inflate_state state;
            uint copy, dist;

            state = (inflate_state)strm.state;

            // if it hasn't been done already, allocate space for the window
            if (state.window == null)
            {
                try
                {
                    state.window = new byte[1 << (int)state.wbits];
                }
                catch (Exception)
                {
                    return 1;
                }
            }

            // if window not in use yet, initialize
            if (state.wsize == 0)
            {
                state.wsize = 1U << (int)state.wbits;
                state.wnext = 0;
                state.whave = 0;
            }

            // copy state.wsize or less output bytes into the circular window
            copy = _out - strm.avail_out;
            if (copy >= state.wsize)
            {
                //memcpy(state.window, strm.next_out-state.wsize, state.wsize);
                Buffer.BlockCopy(strm.out_buf, (int)(strm.next_out - state.wsize), state.window, 0, (int)state.wsize);
                state.wnext = 0;
                state.whave = state.wsize;
            }
            else
            {
                dist = state.wsize - state.wnext;
                if (dist > copy) dist = copy;
                //memcpy(state.window+state.write, strm.next_out-copy, dist);
                Buffer.BlockCopy(strm.out_buf, (int)(strm.next_out - copy), state.window, (int)state.wnext, (int)dist);
                copy -= dist;
                if (copy != 0)
                {
                    //memcpy(state.window, strm.next_out-copy, copy);
                    Buffer.BlockCopy(strm.out_buf, (int)(strm.next_out - copy), state.window, 0, (int)copy);
                    state.wnext = copy;
                    state.whave = state.wsize;
                }
                else
                {
                    state.wnext += dist;
                    if (state.wnext == state.wsize) state.wnext = 0;
                    if (state.whave < state.wsize) state.whave += dist;
                }
            }
            return 0;
        }

        // Macros for inflate():

        // check function to use adler32() for zlib or crc32() for gzip
        //#define UPDATE(check, buf, len) (state.flags ? crc32(check, buf, len) : adler32(check, buf, len))

        // check macros for header crc
        //#define CRC2(check, word) \
        //        hbuf[0] = (byte)word; \
        //        hbuf[1] = (byte)(word >> 8); \
        //        check = crc32(check, hbuf, 2); \

        //#define CRC4(check, word) \
        //        hbuf[0] = (byte)word; \
        //        hbuf[1] = (byte)(word >> 8); \
        //        hbuf[2] = (byte)(word >> 16); \
        //        hbuf[3] = (byte)(word >> 24); \
        //        check = crc32(check, hbuf, 4); \

        // Load registers with state in inflate() for speed
        //#define LOAD() \
        //        put = strm.next_out; \
        //        left = strm.avail_out; \
        //        next = strm.next_in; \
        //        have = strm.avail_in; \
        //        hold = state.hold; \
        //        bits = state.bits;

        // Restore state from registers in inflate()
        //#define RESTORE() \
        //        strm.next_out = put; \
        //        strm.avail_out = left; \
        //        strm.next_in = next; \
        //        strm.avail_in = have; \
        //        state.hold = hold; \
        //        state.bits = bits;

        // Clear the input bit accumulator
        //#define INITBITS() hold = bits = 0;

        // Get a byte of input into the bit accumulator, or return from inflate()
        // if there is no input available.
        //#define PULLBYTE() \
        //        if (have == 0) goto inf_leave; \
        //        have--; \
        //        hold += (uint)in_buf[next++] << (int)bits; \
        //        bits += 8;

        // Assure that there are at least n bits in the bit accumulator.  If there is
        // not enough available input to do that, then return from inflate().
        //#define NEEDBITS(n) while (bits < (unsigned int)(n)) { PULLBYTE(); }

        // Return the low n bits of the bit accumulator (n < 16)
        //#define BITS(n) ((uint)hold & ((1U << (n)) - 1))

        // Remove n bits from the bit accumulator
        //#define DROPBITS(n) \
        //        hold >>= (n); \
        //        bits -= (unsigned int)(n);

        // Remove zero to seven bits as needed to go to a byte boundary
        //#define BYTEBITS() \
        //        hold >>= bits & 7; \
        //        bits -= bits & 7;

        // Reverse the bytes in a 32-bit value
        //#define REVERSE(q) ((((q) >> 24) & 0xff) + (((q) >> 8) & 0xff00) + (((q) & 0xff00) << 8) + (((q) & 0xff) << 24))

        // inflate() uses a state machine to process as much input data and generate as
        // much output data as possible before returning.  The state machine is
        // structured roughly as follows:
        //
        //  for (;;) switch (state) {
        //  ...
        //  case STATEn:
        //      if (not enough input data or output space to make progress)
        //          return;
        //      ... make progress ...
        //      state = STATEm;
        //      break;
        //  ...
        //  }
        //
        // so when inflate() is called again, the same case is attempted again, and
        // if the appropriate resources are provided, the machine proceeds to the
        // next state.  The NEEDBITS() macro is usually the way the state evaluates
        // whether it can proceed or should return.  NEEDBITS() does the return if
        // the requested bits are not available.  The typical use of the BITS macros
        // is:
        //
        //      NEEDBITS(n);
        //      ... do something with BITS(n) ...
        //      DROPBITS(n);
        //
        // where NEEDBITS(n) either returns from inflate() if there isn't enough
        // input left to load n bits into the accumulator, or it continues.  BITS(n)
        // gives the low n bits in the accumulator.  When done, DROPBITS(n) drops
        // the low n bits off the accumulator.  INITBITS() clears the accumulator
        // and sets the number of available bits to zero.  BYTEBITS() discards just
        // enough bits to put the accumulator on a byte boundary.  After BYTEBITS()
        // and a NEEDBITS(8), then BITS(8) would return the next byte in the stream.
        //
        // NEEDBITS(n) uses PULLBYTE() to get an available byte of input, or to return
        // if there is no input available.  The decoding of variable length codes uses
        // PULLBYTE() directly in order to pull just enough bytes to decode the next
        // code, and no more.
        //
        // Some states loop until they get enough input, making sure that enough
        // state information is maintained to continue the loop where it left off
        // if NEEDBITS() returns in the loop.  For example, want, need, and keep
        // would all have to actually be part of the saved state in case NEEDBITS()
        // returns:
        //
        //  case STATEw:
        //      while (want < need) {
        //          NEEDBITS(n);
        //          keep[want++] = BITS(n);
        //          DROPBITS(n);
        //      }
        //      state = STATEx;
        //  case STATEx:
        //
        // As shown above, if the next state is also the next case, then the break
        // is omitted.
        //
        // A state may also return if there is not enough output space available to
        // complete that state.  Those states are copying stored data, writing a
        // literal byte, and copying a matching string.
        //
        // When returning, a "goto inf_leave" is used to update the total counters,
        // update the check value, and determine whether any progress has been made
        // during that inflate() call in order to return the proper return code.
        // Progress is defined as a change in either strm.avail_in or strm.avail_out.
        // When there is a window, goto inf_leave will update the window with the last
        // output written.  If a goto inf_leave occurs in the middle of decompression
        // and there is no window currently, goto inf_leave will create one and copy
        // output to the window for the next call of inflate().
        //
        // In this implementation, the flush parameter of inflate() only affects the
        // return code (per zlib.h).  inflate() always writes as much as possible to
        // strm.next_out, given the space available and the provided input--the effect
        // documented in zlib.h of Z_SYNC_FLUSH.  Furthermore, inflate() always defers
        // the allocation of and copying into a sliding window until necessary, which
        // provides the effect documented in zlib.h for Z_FINISH when the entire input
        // stream available.  So the only thing the flush parameter actually does is:
        // when flush is set to Z_FINISH, inflate() cannot return Z_OK.  Instead it
        // will return Z_BUF_ERROR if it has not reached the end of the stream.

        // permutation of code lengths
        static readonly ushort[] order = new ushort[19] { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

        //   inflate decompresses as much data as possible, and stops when the input
        // buffer becomes empty or the output buffer becomes full. It may introduce
        // some output latency (reading input without producing any output) except when
        // forced to flush.

        // The detailed semantics are as follows. inflate performs one or both of the
        // following actions:

        // - Decompress more input starting at next_in and update next_in and avail_in
        //   accordingly. If not all input can be processed (because there is not
        //   enough room in the output buffer), next_in is updated and processing
        //   will resume at this point for the next call of inflate().

        // - Provide more output starting at next_out and update next_out and avail_out
        //   accordingly.  inflate() provides as much output as possible, until there
        //   is no more input data or no more space in the output buffer (see below
        //   about the flush parameter).

        // Before the call of inflate(), the application should ensure that at least
        // one of the actions is possible, by providing more input and/or consuming
        // more output, and updating the next_* and avail_* values accordingly.
        // The application can consume the uncompressed output when it wants, for
        // example when the output buffer is full (avail_out == 0), or after each
        // call of inflate(). If inflate returns Z_OK and with zero avail_out, it
        // must be called again after making room in the output buffer because there
        // might be more output pending.

        //   The flush parameter of inflate() can be Z_NO_FLUSH, Z_SYNC_FLUSH,
        // Z_FINISH, or Z_BLOCK. Z_SYNC_FLUSH requests that inflate() flush as much
        // output as possible to the output buffer. Z_BLOCK requests that inflate() stop
        // if and when it gets to the next deflate block boundary. When decoding the
        // zlib or gzip format, this will cause inflate() to return immediately after
        // the header and before the first block. When doing a raw inflate, inflate()
        // will go ahead and process the first block, and will return when it gets to
        // the end of that block, or when it runs out of data.

        //   The Z_BLOCK option assists in appending to or combining deflate streams.
        // Also to assist in this, on return inflate() will set strm.data_type to the
        // number of unused bits in the last byte taken from strm.next_in, plus 64
        // if inflate() is currently decoding the last block in the deflate stream,
        // plus 128 if inflate() returned immediately after decoding an end-of-block
        // code or decoding the complete header up to just before the first byte of the
        // deflate stream. The end-of-block will not be indicated until all of the
        // uncompressed data from that block has been written to strm.next_out.  The
        // number of unused bits may in general be greater than seven, except when
        // bit 7 of data_type is set, in which case the number of unused bits will be
        // less than eight.

        //   inflate() should normally be called until it returns Z_STREAM_END or an
        // error. However if all decompression is to be performed in a single step
        // (a single call of inflate), the parameter flush should be set to
        // Z_FINISH. In this case all pending input is processed and all pending
        // output is flushed; avail_out must be large enough to hold all the
        // uncompressed data. (The size of the uncompressed data may have been saved
        // by the compressor for this purpose.) The next operation on this stream must
        // be inflateEnd to deallocate the decompression state. The use of Z_FINISH
        // is never required, but can be used to inform inflate that a faster approach
        // may be used for the single inflate() call.

        //    In this implementation, inflate() always flushes as much output as
        // possible to the output buffer, and always uses the faster approach on the
        // first call. So the only effect of the flush parameter in this implementation
        // is on the return value of inflate(), as noted below, or when it returns early
        // because Z_BLOCK is used.

        //    If a preset dictionary is needed after this call (see inflateSetDictionary
        // below), inflate sets strm.adler to the adler32 checksum of the dictionary
        // chosen by the compressor and returns Z_NEED_DICT; otherwise it sets
        // strm.adler to the adler32 checksum of all output produced so far (that is,
        // total_out bytes) and returns Z_OK, Z_STREAM_END or an error code as described
        // below. At the end of the stream, inflate() checks that its computed adler32
        // checksum is equal to that saved by the compressor and returns Z_STREAM_END
        // only if the checksum is correct.

        //   inflate() will decompress and check either zlib-wrapped or gzip-wrapped
        // deflate data.  The header type is detected automatically.  Any information
        // contained in the gzip header is not retained, so applications that need that
        // information should instead use raw inflate, see inflateInit2() below, or
        // inflateBack() and perform their own processing of the gzip header and
        // trailer.

        //   inflate() returns Z_OK if some progress has been made (more input processed
        // or more output produced), Z_STREAM_END if the end of the compressed data has
        // been reached and all uncompressed output has been produced, Z_NEED_DICT if a
        // preset dictionary is needed at this point, Z_DATA_ERROR if the input data was
        // corrupted (input stream not conforming to the zlib format or incorrect check
        // value), Z_STREAM_ERROR if the stream structure was inconsistent (for example
        // if next_in or next_out was NULL), Z_MEM_ERROR if there was not enough memory,
        // Z_BUF_ERROR if no progress is possible or if there was not enough room in the
        // output buffer when Z_FINISH is used. Note that Z_BUF_ERROR is not fatal, and
        // inflate() can be called again with more input and more output space to
        // continue decompressing. If Z_DATA_ERROR is returned, the application may then
        // call inflateSync() to look for a good compression block if a partial recovery
        // of the data is desired.

        public static int inflate(z_stream strm, int flush)
        {
            inflate_state state;
            uint next;              // next input
            int put;                // next output
            uint have, left;        // available input and output
            uint hold;              // bit buffer
            uint bits;              // bits in bit buffer
            uint _in, _out;         // save starting available input and output
            uint copy;              // number of stored or match bytes to copy
            byte[] from;            // where to copy match bytes from
            int from_ind;           // where to copy match bytes from
            code here;              // current decoding table entry
            code last;              // parent table entry
            uint len;               // length to copy for repeats, bits to drop
            int ret;                // return code
            byte[] hbuf = new byte[4];// buffer for gzip header crc calculation

            if (strm == null || strm.state == null || strm.out_buf == null || (strm.in_buf == null && strm.avail_in != 0))
                return Z_STREAM_ERROR;

            byte[] in_buf = strm.in_buf;
            byte[] out_buf = strm.out_buf;

            state = (inflate_state)strm.state;
            if (state.mode == inflate_mode.TYPE) state.mode = inflate_mode.TYPEDO; // skip check

            //was LOAD();
            put = strm.next_out;
            left = strm.avail_out;
            next = strm.next_in;
            have = strm.avail_in;
            hold = state.hold;
            bits = state.bits;

            _in = have;
            _out = left;
            ret = Z_OK;

            for (;;)
            {
                switch (state.mode)
                {
                    case inflate_mode.HEAD:
                        if (state.wrap == 0)
                        {
                            state.mode = inflate_mode.TYPEDO;
                            break;
                        }
                        //was NEEDBITS(16);
                        while (bits < 16)
                        {
                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }

                        if ((state.wrap & 2) != 0 && hold == 0x8b1f)
                        {  // gzip header
                            state.check = crc32(0, null, 0);

                            //was CRC2(state.check, hold);
                            hbuf[0] = (byte)hold;
                            hbuf[1] = (byte)(hold >> 8);
                            state.check = crc32(state.check, hbuf, 2);

                            //was INITBITS();
                            hold = bits = 0;

                            state.mode = inflate_mode.FLAGS;
                            break;
                        }

                        state.flags = 0;                    // expect zlib header
                        if (state.head != null) state.head.done = -1;

                        //was if(!(state.wrap & 1) ||	// check if zlib header allowed
                        // ((BITS(8)<<8)+(hold>>8))%31)
                        if ((state.wrap & 1) == 0 ||            // check if zlib header allowed
                            ((((hold & 0xFF) << 8) + (hold >> 8)) % 31) != 0)
                        {
                            strm.msg = "incorrect header check";
                            state.mode = inflate_mode.BAD;
                            break;
                        }

                        //was if(BITS(4)!=Z_DEFLATED)
                        if ((hold & 0x0F) != Z_DEFLATED)
                        {
                            strm.msg = "unknown compression method";
                            state.mode = inflate_mode.BAD;
                            break;
                        }

                        //was DROPBITS(4);
                        hold >>= 4;
                        bits -= 4;

                        //was len=BITS(4)+8;
                        len = (hold & 0x0F) + 8;

                        if (state.wbits == 0)
                            state.wbits = len;
                        else if (len > state.wbits)
                        {
                            strm.msg = "invalid window size";
                            state.mode = inflate_mode.BAD;
                            break;
                        }

                        state.dmax = 1U << (int)len;
                        //Tracev((stderr, "inflate:   zlib header ok\n"));

                        strm.adler = state.check = adler32(0, null, 0);
                        state.mode = (hold & 0x200) != 0 ? inflate_mode.DICTID : inflate_mode.TYPE;

                        //was INITBITS();
                        hold = bits = 0;

                        break;
                    case inflate_mode.FLAGS:
                        //was NEEDBITS(16);
                        while (bits < 16)
                        {
                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }

                        state.flags = (int)hold;
                        if ((state.flags & 0xff) != Z_DEFLATED)
                        {
                            strm.msg = "unknown compression method";
                            state.mode = inflate_mode.BAD;
                            break;
                        }
                        if ((state.flags & 0xe000) != 0)
                        {
                            strm.msg = "unknown header flags set";
                            state.mode = inflate_mode.BAD;
                            break;
                        }
                        if (state.head != null) state.head.text = (int)((hold >> 8) & 1);
                        if ((state.flags & 0x0200) != 0)
                        {
                            //was CRC2(state.check, hold);
                            hbuf[0] = (byte)hold;
                            hbuf[1] = (byte)(hold >> 8);
                            state.check = crc32(state.check, hbuf, 2);
                        }

                        //was INITBITS();
                        hold = bits = 0;

                        state.mode = inflate_mode.TIME;
                        break; // no fall through
                    case inflate_mode.TIME:
                        //was NEEDBITS(32);
                        while (bits < 32)
                        {
                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }

                        if (state.head != null) state.head.time = hold;
                        if ((state.flags & 0x0200) != 0)
                        {
                            //was CRC4(state.check, hold);
                            hbuf[0] = (byte)hold;
                            hbuf[1] = (byte)(hold >> 8);
                            hbuf[2] = (byte)(hold >> 16);
                            hbuf[3] = (byte)(hold >> 24);
                            state.check = crc32(state.check, hbuf, 4);
                        }

                        //was INITBITS();
                        hold = bits = 0;

                        state.mode = inflate_mode.OS;
                        break; // no fall through
                    case inflate_mode.OS:
                        //was NEEDBITS(16);
                        while (bits < 16)
                        {
                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }

                        if (state.head != null)
                        {
                            state.head.xflags = (int)(hold & 0xff);
                            state.head.os = (int)(hold >> 8);
                        }
                        if ((state.flags & 0x0200) != 0)
                        {
                            //was CRC2(state.check, hold);
                            hbuf[0] = (byte)hold;
                            hbuf[1] = (byte)(hold >> 8);
                            state.check = crc32(state.check, hbuf, 2);
                        }

                        //was INITBITS();
                        hold = bits = 0;

                        state.mode = inflate_mode.EXLEN;
                        break; // no fall through
                    case inflate_mode.EXLEN:
                        if ((state.flags & 0x0400) != 0)
                        {
                            //was NEEDBITS(16);
                            while (bits < 16)
                            {
                                //was PULLBYTE();
                                if (have == 0) goto inf_leave;
                                have--;
                                hold += (uint)in_buf[next++] << (int)bits;
                                bits += 8;
                            }

                            state.length = hold;
                            if (state.head != null) state.head.extra_len = hold;
                            if ((state.flags & 0x0200) != 0)
                            {
                                //was CRC2(state.check, hold);
                                hbuf[0] = (byte)hold;
                                hbuf[1] = (byte)(hold >> 8);
                                state.check = crc32(state.check, hbuf, 2);
                            }

                            //was INITBITS();
                            hold = bits = 0;
                        }
                        else if (state.head != null) state.head.extra = null;

                        state.mode = inflate_mode.EXTRA;
                        break; // no fall through
                    case inflate_mode.EXTRA:
                        if ((state.flags & 0x0400) != 0)
                        {
                            copy = state.length;
                            if (copy > have) copy = have;
                            if (copy != 0)
                            {
                                if (state.head != null && state.head.extra != null)
                                {
                                    len = state.head.extra_len - state.length; // should be always zero!!!
                                                                               //was zmemcpy(state.head.extra+len, next, (len+copy>state.head.extra_max)?state.head.extra_max-len:copy);
                                    Buffer.BlockCopy(in_buf, (int)next, state.head.extra, (int)len, (int)((len + copy > state.head.extra_max) ? state.head.extra_max - len : copy));
                                }
                                if ((state.flags & 0x0200) != 0) state.check = crc32(state.check, in_buf, (uint)next, copy);
                                have -= copy;
                                next += copy;
                                state.length -= copy;
                            }
                            if (state.length != 0) goto inf_leave;
                        }
                        state.length = 0;
                        state.mode = inflate_mode.NAME;
                        break; // no fall through
                    case inflate_mode.NAME:
                        if ((state.flags & 0x0800) != 0)
                        {
                            if (have == 0) goto inf_leave;
                            copy = 0;
                            do
                            {
                                //was len=next[copy++];
                                len = in_buf[next + copy++];
                                if (state.head != null && state.head.name != null && state.length < state.head.name_max)
                                    state.head.name[state.length++] = (byte)len;
                            } while (len != 0 && copy < have);

                            if ((state.flags & 0x0200) != 0) state.check = crc32(state.check, in_buf, (uint)next, copy);
                            have -= copy;
                            next += copy;
                            if (len != 0) goto inf_leave;
                        }
                        else if (state.head != null) state.head.name = null;
                        state.length = 0;
                        state.mode = inflate_mode.COMMENT;
                        break; // no fall through
                    case inflate_mode.COMMENT:
                        if ((state.flags & 0x1000) != 0)
                        {
                            if (have == 0) goto inf_leave;
                            copy = 0;
                            do
                            {
                                //was len=next[copy++];
                                len = in_buf[next + copy++];
                                if (state.head != null && state.head.comment != null && state.length < state.head.comm_max)
                                    state.head.comment[state.length++] = (byte)len;
                            } while (len != 0 && copy < have);
                            if ((state.flags & 0x0200) != 0) state.check = crc32(state.check, in_buf, (uint)next, copy);
                            have -= copy;
                            next += copy;
                            if (len != 0) goto inf_leave;
                        }
                        else if (state.head != null) state.head.comment = null;
                        state.mode = inflate_mode.HCRC;
                        break; // no fall through
                    case inflate_mode.HCRC:
                        if ((state.flags & 0x0200) != 0)
                        {
                            //was NEEDBITS(16);
                            while (bits < 16)
                            {
                                //was PULLBYTE();
                                if (have == 0) goto inf_leave;
                                have--;
                                hold += (uint)in_buf[next++] << (int)bits;
                                bits += 8;
                            }

                            if (hold != (state.check & 0xffff))
                            {
                                strm.msg = "header crc mismatch";
                                state.mode = inflate_mode.BAD;
                                break;
                            }

                            //was INITBITS();
                            hold = bits = 0;
                        }
                        if (state.head != null)
                        {
                            state.head.hcrc = (int)((state.flags >> 9) & 1);
                            state.head.done = 1;
                        }
                        strm.adler = state.check = crc32(0, null, 0);
                        state.mode = inflate_mode.TYPE;
                        break;
                    case inflate_mode.DICTID:
                        //was NEEDBITS(32);
                        while (bits < 32)
                        {
                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }

                        //was strm.adler=state.check=REVERSE(hold);
                        strm.adler = state.check = ((hold >> 24) & 0xff) + ((hold >> 8) & 0xff00) + ((hold & 0xff00) << 8) + ((hold & 0xff) << 24);

                        //was INITBITS();
                        hold = bits = 0;

                        state.mode = inflate_mode.DICT;
                        break; // no fall through
                    case inflate_mode.DICT:
                        if (state.havedict == 0)
                        {
                            //was RESTORE();
                            strm.next_out = put;
                            strm.avail_out = left;
                            strm.next_in = next;
                            strm.avail_in = have;
                            state.hold = hold;
                            state.bits = bits;

                            return Z_NEED_DICT;
                        }
                        strm.adler = state.check = adler32(0, null, 0);
                        state.mode = inflate_mode.TYPE;
                        break; // no fall through
                    case inflate_mode.TYPE:
                        if (flush == Z_BLOCK || flush == Z_TREES) goto inf_leave;

                        state.mode = inflate_mode.TYPEDO;
                        break; // no fall through
                    case inflate_mode.TYPEDO:
                        if (state.last != 0)
                        {
                            //was BYTEBITS();
                            hold >>= (int)(bits & 7);
                            bits -= bits & 7;

                            state.mode = inflate_mode.CHECK;
                            break;
                        }

                        //was NEEDBITS(3);
                        while (bits < 3)
                        {
                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }

                        //was state.last=BITS(1);
                        state.last = (int)(hold & 0x01);

                        //was DROPBITS(1);
                        hold >>= 1;
                        bits -= 1;

                        //was switch(BITS(2))
                        switch (hold & 0x03)
                        {
                            case 0: // stored block
                                    //Tracev((stderr, "inflate:     stored block%s\n", state.last ? " (last)" : ""));
                                state.mode = inflate_mode.STORED;
                                break;
                            case 1: // fixed block
                                fixedtables(state);
                                //Tracev((stderr, "inflate:     fixed codes block%s\n", state.last ? " (last)" : ""));
                                state.mode = inflate_mode.LEN_;              // decode codes
                                if (flush == Z_TREES)
                                {
                                    //was DROPBITS(2);
                                    hold >>= 2;
                                    bits -= 2;
                                    goto inf_leave;
                                }
                                break;
                            case 2: // dynamic block
                                    //Tracev((stderr, "inflate:     dynamic codes block%s\n", state.last ? " (last)" : ""));
                                state.mode = inflate_mode.TABLE;
                                break;
                            case 3:
                                strm.msg = "invalid block type";
                                state.mode = inflate_mode.BAD;
                                break;
                        }

                        //was DROPBITS(2);
                        hold >>= 2;
                        bits -= 2;

                        break;
                    case inflate_mode.STORED:
                        //was BYTEBITS(); // go to byte boundary
                        hold >>= (int)(bits & 7);
                        bits -= bits & 7;

                        //was NEEDBITS(32);
                        while (bits < 32)
                        {
                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }

                        if ((hold & 0xffff) != ((hold >> 16) ^ 0xffff))
                        {
                            strm.msg = "invalid stored block lengths";
                            state.mode = inflate_mode.BAD;
                            break;
                        }
                        state.length = hold & 0xffff;
                        //Tracev((stderr, "inflate:       stored length %u\n", state.length));

                        //was INITBITS();
                        hold = bits = 0;

                        state.mode = inflate_mode.COPY_;
                        if (flush == Z_TREES) goto inf_leave;
                        break; // no fall through
                    case inflate_mode.COPY_:
                        state.mode = inflate_mode.COPY;
                        break; // no fall through
                    case inflate_mode.COPY:
                        copy = state.length;
                        if (copy != 0)
                        {
                            if (copy > have) copy = have;
                            if (copy > left) copy = left;
                            if (copy == 0) goto inf_leave;

                            //was memcpy(put, next, copy);
                            Buffer.BlockCopy(in_buf, (int)next, out_buf, put, (int)copy);

                            have -= copy;
                            next += copy;
                            left -= copy;
                            put += (int)copy;
                            state.length -= copy;
                            break;
                        }
                        //Tracev((stderr, "inflate:       stored end\n"));
                        state.mode = inflate_mode.TYPE;
                        break;
                    case inflate_mode.TABLE:
                        //was NEEDBITS(14);
                        while (bits < 14)
                        {
                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }

                        //was state.nlen=BITS(5)+257;
                        state.nlen = (hold & 0x1f) + 257;

                        //was DROPBITS(5);
                        hold >>= 5;
                        bits -= 5;

                        //was state.ndist=BITS(5)+1;
                        state.ndist = (hold & 0x1f) + 1;

                        //was DROPBITS(5);
                        hold >>= 5;
                        bits -= 5;

                        //was state.ncode=BITS(4)+4;
                        state.ncode = (hold & 0x0f) + 4;

                        //was DROPBITS(4);
                        hold >>= 4;
                        bits -= 4;

                        if (state.nlen > 286 || state.ndist > 30)
                        {
                            strm.msg = "too many length or distance symbols";
                            state.mode = inflate_mode.BAD;
                            break;
                        }
                        //Tracev((stderr, "inflate:       table sizes ok\n"));
                        state.have = 0;
                        state.mode = inflate_mode.LENLENS;
                        break; // no fall through
                    case inflate_mode.LENLENS:
                        while (state.have < state.ncode)
                        {
                            //was NEEDBITS(3);
                            while (bits < 3)
                            {
                                //was PULLBYTE();
                                if (have == 0) goto inf_leave;
                                have--;
                                hold += (uint)in_buf[next++] << (int)bits;
                                bits += 8;
                            }

                            //was state.lens[order[state.have++]]=(ushort)BITS(3);
                            state.lens[order[state.have++]] = (ushort)(hold & 0x07);

                            //was DROPBITS(3);
                            hold >>= 3;
                            bits -= 3;
                        }
                        while (state.have < 19) state.lens[order[state.have++]] = 0;
                        state.next = 0;
                        state.lencode = state.codes;
                        state.lenbits = 7;

                        //was ret=inflate_table(codetype.CODES, state.lens, 19, &(state.next), &(state.lenbits), state.work);
                        ret = inflate_table(codetype.CODES, state.lens, 0, 19, state.codes, ref state.next, ref state.lenbits, state.work);

                        if (ret != 0)
                        {
                            strm.msg = "invalid code lengths set";
                            state.mode = inflate_mode.BAD;
                            break;
                        }
                        //Tracev((stderr, "inflate:       code lengths ok\n"));
                        state.have = 0;
                        state.mode = inflate_mode.CODELENS;
                        break; // no fall through
                    case inflate_mode.CODELENS:
                        while (state.have < (state.nlen + state.ndist))
                        {
                            for (;;)
                            {
                                //was _this=state.lencode[BITS(state.lenbits)];
                                here = state.lencode[hold & ((1 << (int)state.lenbits) - 1)];

                                if (here.bits <= bits) break;

                                //was PULLBYTE();
                                if (have == 0) goto inf_leave;
                                have--;
                                hold += (uint)in_buf[next++] << (int)bits;
                                bits += 8;
                            }

                            if (here.val < 16)
                            {
                                //was NEEDBITS(_this.bits);
                                while (bits < here.bits)
                                {
                                    //was PULLBYTE();
                                    if (have == 0) goto inf_leave;
                                    have--;
                                    hold += (uint)in_buf[next++] << (int)bits;
                                    bits += 8;
                                }

                                //was DROPBITS(_this.bits);
                                hold >>= here.bits;
                                bits -= here.bits;

                                state.lens[state.have++] = here.val;
                            }
                            else
                            {
                                if (here.val == 16)
                                {
                                    //was NEEDBITS(_this.bits+2);
                                    while (bits < here.bits + 2)
                                    {
                                        //was PULLBYTE();
                                        if (have == 0) goto inf_leave;
                                        have--;
                                        hold += (uint)in_buf[next++] << (int)bits;
                                        bits += 8;
                                    }

                                    //was DROPBITS(_this.bits);
                                    hold >>= here.bits;
                                    bits -= here.bits;

                                    if (state.have == 0)
                                    {
                                        strm.msg = "invalid bit length repeat";
                                        state.mode = inflate_mode.BAD;
                                        break;
                                    }
                                    len = state.lens[state.have - 1];

                                    //was copy=3+BITS(2);
                                    copy = 3 + (hold & 0x03);

                                    //was DROPBITS(2);
                                    hold >>= 2;
                                    bits -= 2;
                                }
                                else if (here.val == 17)
                                {
                                    //was NEEDBITS(_this.bits+3);
                                    while (bits < here.bits + 3)
                                    {
                                        //was PULLBYTE();
                                        if (have == 0) goto inf_leave;
                                        have--;
                                        hold += (uint)in_buf[next++] << (int)bits;
                                        bits += 8;
                                    }

                                    //was DROPBITS(_this.bits);
                                    hold >>= here.bits;
                                    bits -= here.bits;

                                    len = 0;

                                    //was copy=3+BITS(3);
                                    copy = 3 + (hold & 0x07);

                                    //was DROPBITS(3);
                                    hold >>= 3;
                                    bits -= 3;
                                }
                                else
                                {
                                    //was NEEDBITS(_this.bits+7);
                                    while (bits < here.bits + 7)
                                    {
                                        //was PULLBYTE();
                                        if (have == 0) goto inf_leave;
                                        have--;
                                        hold += (uint)in_buf[next++] << (int)bits;
                                        bits += 8;
                                    }

                                    //was DROPBITS(_this.bits);
                                    hold >>= here.bits;
                                    bits -= here.bits;

                                    len = 0;

                                    //was copy=11+BITS(7);
                                    copy = 11 + (hold & 0x7F);

                                    //was DROPBITS(7);
                                    hold >>= 7;
                                    bits -= 7;
                                }

                                if (state.have + copy > state.nlen + state.ndist)
                                {
                                    strm.msg = "invalid bit length repeat";
                                    state.mode = inflate_mode.BAD;
                                    break;
                                }

                                while ((copy--) != 0) state.lens[state.have++] = (ushort)len;
                            }
                        }

                        // handle error breaks in while
                        if (state.mode == inflate_mode.BAD) break;

                        // check for end-of-block code (better have one)
                        if (state.lens[256] == 0)
                        {
                            strm.msg = "invalid code -- missing end-of-block";
                            state.mode = inflate_mode.BAD;
                            break;
                        }

                        // build code tables -- note: do not change the lenbits or distbits
                        // values here (9 and 6) without reading the comments in inftrees.h
                        // concerning the ENOUGH constants, which depend on those values

                        state.next = 0;
                        state.lencode = state.codes;
                        state.lenbits = 9;

                        //was ret=inflate_table(codetype.LENS, state.lens, state.nlen, &(state.next), &(state.lenbits), state.work);
                        ret = inflate_table(codetype.LENS, state.lens, 0, state.nlen, state.codes, ref state.next, ref state.lenbits, state.work);

                        if (ret != 0)
                        {
                            strm.msg = "invalid literal/lengths set";
                            state.mode = inflate_mode.BAD;
                            break;
                        }

                        //was state.distcode = (code const *)(state.next);
                        state.distcode = state.codes;
                        state.distcode_ind = state.next;

                        state.distbits = 6;

                        //was ret=inflate_table(codetype.DISTS, state.lens+state.nlen, state.ndist, &(state.next), &(state.distbits), state.work);
                        ret = inflate_table(codetype.DISTS, state.lens, (int)state.nlen, state.ndist, state.codes, ref state.next, ref state.distbits, state.work);

                        if (ret != 0)
                        {
                            strm.msg = "invalid distances set";
                            state.mode = inflate_mode.BAD;
                            break;
                        }

                        //Tracev((stderr, "inflate:       codes ok\n"));
                        state.mode = inflate_mode.LEN_;
                        if (flush == Z_TREES) goto inf_leave;
                        break; // no fall through
                    case inflate_mode.LEN_:
                        state.mode = inflate_mode.LEN;
                        break; // no fall through
                    case inflate_mode.LEN:
                        if (have >= 6 && left >= 258)
                        {
                            //was RESTORE();
                            strm.next_out = put;
                            strm.avail_out = left;
                            strm.next_in = next;
                            strm.avail_in = have;
                            state.hold = hold;
                            state.bits = bits;

                            inflate_fast(strm, _out);

                            //was LOAD();
                            put = strm.next_out;
                            left = strm.avail_out;
                            next = strm.next_in;
                            have = strm.avail_in;
                            hold = state.hold;
                            bits = state.bits;
                            if (state.mode == inflate_mode.TYPE)
                                state.back = -1;
                            break;
                        }
                        state.back = 0;
                        for (;;)
                        {
                            //was _this=state.lencode[BITS(state.lenbits)];
                            here = state.lencode[hold & ((1 << (int)state.lenbits) - 1)];
                            if (here.bits <= bits) break;

                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }
                        if (here.op != 0 && (here.op & 0xf0) == 0)
                        {
                            last = here;
                            for (;;)
                            {
                                //was _this=state.lencode[last.val+(BITS(last.bits+last.op)>>last.bits)];
                                here = state.lencode[last.val + ((hold & ((1 << (int)(last.bits + last.op)) - 1)) >> last.bits)];
                                if ((last.bits + here.bits) <= bits) break;

                                //was PULLBYTE();
                                if (have == 0) goto inf_leave;
                                have--;
                                hold += (uint)in_buf[next++] << (int)bits;
                                bits += 8;
                            }

                            //was DROPBITS(last.bits);
                            hold >>= last.bits;
                            bits -= last.bits;
                            state.back += last.bits;
                        }

                        //was DROPBITS(here.bits);
                        hold >>= here.bits;
                        bits -= here.bits;

                        state.back += here.bits;
                        state.length = here.val;
                        if (here.op == 0)
                        {
                            //Tracevv((stderr, this.val >= 0x20 && this.val < 0x7f ? "inflate:         literal '%c'\n" : "inflate:         literal 0x%02x\n", this.val));
                            state.mode = inflate_mode.LIT;
                            break;
                        }
                        if ((here.op & 32) != 0)
                        {
                            //Tracevv((stderr, "inflate:         end of block\n"));
                            state.back = -1;
                            state.mode = inflate_mode.TYPE;
                            break;
                        }
                        if ((here.op & 64) != 0)
                        {
                            strm.msg = "invalid literal/length code";
                            state.mode = inflate_mode.BAD;
                            break;
                        }
                        state.extra = (uint)(here.op & 15);
                        state.mode = inflate_mode.LENEXT;
                        break; // no fall through
                    case inflate_mode.LENEXT:
                        if (state.extra != 0)
                        {
                            //was NEEDBITS(state.extra);
                            while (bits < state.extra)
                            {
                                //was PULLBYTE();
                                if (have == 0) goto inf_leave;
                                have--;
                                hold += (uint)in_buf[next++] << (int)bits;
                                bits += 8;
                            }

                            //was state.length+=BITS(state.extra);
                            state.length += (uint)(hold & ((1 << (int)state.extra) - 1));

                            //was DROPBITS(state.extra);
                            hold >>= (int)state.extra;
                            bits -= state.extra;
                            state.back += (int)state.extra;
                        }
                        //Tracevv((stderr, "inflate:         length %u\n", state.length));
                        state.was = state.length;
                        state.mode = inflate_mode.DIST;
                        break; // no fall through
                    case inflate_mode.DIST:
                        for (;;)
                        {
                            //was _this=state.distcode[BITS(state.distbits)];
                            here = state.distcode[state.distcode_ind + (hold & ((1 << (int)state.distbits) - 1))];
                            if (here.bits <= bits) break;

                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }
                        if ((here.op & 0xf0) == 0)
                        {
                            last = here;
                            for (;;)
                            {
                                //was _this=state.distcode[last.val+(BITS(last.bits+last.op)>>last.bits)];
                                here = state.distcode[state.distcode_ind + last.val + ((hold & ((1 << (int)(last.bits + last.op)) - 1)) >> last.bits)];
                                if ((last.bits + here.bits) <= bits) break;

                                //was PULLBYTE();
                                if (have == 0) goto inf_leave;
                                have--;
                                hold += (uint)in_buf[next++] << (int)bits;
                                bits += 8;
                            }

                            //was DROPBITS(last.bits);
                            hold >>= last.bits;
                            bits -= last.bits;
                            state.back += last.bits;
                        }

                        //was DROPBITS(_this.bits);
                        hold >>= here.bits;
                        bits -= here.bits;
                        state.back += here.bits;

                        if ((here.op & 64) != 0)
                        {
                            strm.msg = "invalid distance code";
                            state.mode = inflate_mode.BAD;
                            break;
                        }
                        state.offset = here.val;
                        state.extra = (uint)(here.op & 15);
                        state.mode = inflate_mode.DISTEXT;
                        break; // no fall through
                    case inflate_mode.DISTEXT:
                        if (state.extra != 0)
                        {
                            //was NEEDBITS(state.extra);
                            while (bits < state.extra)
                            {
                                //was PULLBYTE();
                                if (have == 0) goto inf_leave;
                                have--;
                                hold += (uint)in_buf[next++] << (int)bits;
                                bits += 8;
                            }

                            //was state.offset+=BITS(state.extra);
                            state.offset += (uint)(hold & ((1 << (int)state.extra) - 1));

                            //was DROPBITS(state.extra);
                            hold >>= (int)state.extra;
                            bits -= state.extra;
                            state.back += (int)state.extra;
                        }

                        if (state.offset > state.dmax)
                        {
                            strm.msg = "invalid distance too far back (STRICT)";
                            state.mode = inflate_mode.BAD;
                            break;
                        }

                        //Tracevv((stderr, "inflate:         distance %u\n", state.offset));
                        state.mode = inflate_mode.MATCH;
                        break; // no fall through
                    case inflate_mode.MATCH:
                        if (left == 0) goto inf_leave;

                        copy = _out - left;
                        if (state.offset > copy)
                        { // copy from window
                            copy = state.offset - copy;
                            if (copy > state.whave)
                            {
                                if (state.sane)
                                {
                                    strm.msg = "invalid distance too far back";
                                    state.mode = inflate_mode.BAD;
                                    break;
                                }
#if INFLATE_ALLOW_INVALID_DISTANCE_TOOFAR_ARRR
								//Trace((stderr, "inflate.c too far\n"));
								copy-=state.whave;
								if(copy>state.length) copy=state.length;
								if(copy>left) copy=left;
								left-=copy;
								state.length-=copy;
								do
								{
									out_buf[put++]=0;
								} while(--copy!=0);
								if(state.length==0) state.mode=inflate_mode.LEN;
								break;
#endif
                            }
                            if (copy > state.wnext)
                            {
                                copy -= state.wnext;
                                from = state.window;
                                from_ind = (int)(state.wsize - copy);
                            }
                            else
                            {
                                from = state.window;
                                from_ind = (int)(state.wnext - copy);
                            }
                            if (copy > state.length) copy = state.length;
                        }
                        else
                        { // copy from output
                            from = out_buf;
                            from_ind = (int)(put - state.offset);
                            copy = state.length;
                        }
                        if (copy > left) copy = left;
                        left -= copy;
                        state.length -= copy;
                        do
                        {
                            out_buf[put++] = from[from_ind++];
                        } while (--copy != 0);
                        if (state.length == 0) state.mode = inflate_mode.LEN;
                        break;
                    case inflate_mode.LIT:
                        if (left == 0) goto inf_leave;
                        out_buf[put++] = (byte)(state.length);
                        left--;
                        state.mode = inflate_mode.LEN;
                        break;
                    case inflate_mode.CHECK:
                        if (state.wrap != 0)
                        {
                            //was NEEDBITS(32);
                            while (bits < 32)
                            {
                                //was PULLBYTE();
                                if (have == 0) goto inf_leave;
                                have--;
                                hold += (uint)in_buf[next++] << (int)bits;
                                bits += 8;
                            }

                            _out -= left;
                            strm.total_out += _out;
                            state.total += _out;
                            if (out_buf != null) strm.adler = state.check = (state.flags != 0 ? crc32(state.check, out_buf, (uint)(put - _out), _out) : adler32(state.check, out_buf, (uint)(put - _out), _out));

                            _out = left;
                            //was if((state.flags ? hold :REVERSE(hold))!=state.check)
                            if ((state.flags != 0 ? hold : ((hold >> 24) & 0xff) + ((hold >> 8) & 0xff00) + ((hold & 0xff00) << 8) + ((hold & 0xff) << 24)) != state.check)
                            {
                                strm.msg = "incorrect data check";
                                state.mode = inflate_mode.BAD;
                                break;
                            }

                            //was INITBITS();
                            hold = bits = 0;

                            //Tracev((stderr, "inflate:   check matches trailer\n"));
                        }

                        state.mode = (state.wrap != 0 && state.flags != 0 ? inflate_mode.LENGTH : inflate_mode.DONE);

                        break; // no fall through
                    case inflate_mode.LENGTH:
                        //was NEEDBITS(32);
                        while (bits < 32)
                        {
                            //was PULLBYTE();
                            if (have == 0) goto inf_leave;
                            have--;
                            hold += (uint)in_buf[next++] << (int)bits;
                            bits += 8;
                        }

                        if (hold != (state.total & 0xffffffffU))
                        {
                            strm.msg = "incorrect length check";
                            state.mode = inflate_mode.BAD;
                            break;
                        }

                        //was INITBITS();
                        hold = bits = 0;

                        //Tracev((stderr, "inflate:   length matches trailer\n"));

                        state.mode = inflate_mode.DONE;
                        break; // no fall through
                    case inflate_mode.DONE:
                        ret = Z_STREAM_END;
                        goto inf_leave;
                    case inflate_mode.BAD:
                        ret = Z_DATA_ERROR;
                        goto inf_leave;
                    case inflate_mode.MEM: return Z_MEM_ERROR;
                    case inflate_mode.SYNC: return Z_STREAM_ERROR;
                    default: return Z_STREAM_ERROR;
                } // switch(state.mode)
            } // for(; ; )

            // Return from inflate(), updating the total counts and the check value.
            // If there was no progress during the inflate() call, return a buffer
            // error.  Call updatewindow() to create and/or update the window state.
            // Note: a memory error from inflate() is non-recoverable.

            inf_leave:
            //was RESTORE();
            strm.next_out = put;
            strm.avail_out = left;
            strm.next_in = next;
            strm.avail_in = have;
            state.hold = hold;
            state.bits = bits;

            if (state.wsize != 0 || (state.mode < inflate_mode.CHECK && _out != strm.avail_out))
            {
                if (updatewindow(strm, _out) != 0)
                {
                    state.mode = inflate_mode.MEM;
                    return Z_MEM_ERROR;
                }
            }
            _in -= strm.avail_in;
            _out -= strm.avail_out;
            strm.total_in += _in;
            strm.total_out += _out;
            state.total += _out;
            if (state.wrap != 0 && _out != 0) strm.adler = state.check = (state.flags != 0 ? crc32(state.check, out_buf, (uint)(strm.next_out - _out), _out) : adler32(state.check, out_buf, (uint)(strm.next_out - _out), _out));

            //??? strm.data_type=state.bits+(state.last!=0?64:0)+(state.mode==inflate_mode.TYPE?128:0);

            if (((_in == 0 && _out == 0) || flush == Z_FINISH) && ret == Z_OK) ret = Z_BUF_ERROR;

            return ret;
        }

        //   All dynamically allocated data structures for this stream are freed.
        // This function discards any unprocessed input and does not flush any
        // pending output.

        //   inflateEnd returns Z_OK if success, Z_STREAM_ERROR if the stream state
        // was inconsistent. In the error case, msg may be set but then points to a
        // static string (which must not be deallocated).
        public static int inflateEnd(z_stream strm)
        {
            if (strm == null || strm.state == null) return Z_STREAM_ERROR;
            inflate_state state = (inflate_state)strm.state;
            state.window = null;
            strm.state = null;
            //Tracev((stderr, "inflate: end\n"));
            return Z_OK;
        }

        //   Initializes the decompression dictionary from the given uncompressed byte
        // sequence. This function must be called immediately after a call of inflate,
        // if that call returned Z_NEED_DICT. The dictionary chosen by the compressor
        // can be determined from the adler32 value returned by that call of inflate.
        // The compressor and decompressor must use exactly the same dictionary (see
        // deflateSetDictionary).  For raw inflate, this function can be called
        // immediately after inflateInit2() or inflateReset() and before any call of
        // inflate() to set the dictionary.  The application must insure that the
        // dictionary that was used for compression is provided.

        //   inflateSetDictionary returns Z_OK if success, Z_STREAM_ERROR if a
        // parameter is invalid (such as NULL dictionary) or the stream state is
        // inconsistent, Z_DATA_ERROR if the given dictionary doesn't match the
        // expected one (incorrect adler32 value). inflateSetDictionary does not
        // perform any decompression: this will be done by subsequent calls of
        // inflate().

        public static int inflateSetDictionary(z_stream strm, byte[] dictionary, uint dictLength)
        {
            // check state
            if (strm == null || strm.state == null) return Z_STREAM_ERROR;

            inflate_state state = (inflate_state)strm.state;
            if (state.wrap != 0 && state.mode != inflate_mode.DICT) return Z_STREAM_ERROR;

            // check for correct dictionary id
            if (state.mode == inflate_mode.DICT)
            {
                uint id = adler32(0, null, 0);
                id = adler32(id, dictionary, dictLength);
                if (id != state.check) return Z_DATA_ERROR;
            }

            // copy dictionary to window
            if (updatewindow(strm, strm.avail_out) != 0)
            {
                state.mode = inflate_mode.MEM;
                return Z_MEM_ERROR;
            }
            if (dictLength > state.wsize)
            {
                //was memcpy(state.window, dictionary+dictLength-state.wsize, state.wsize);
                Buffer.BlockCopy(dictionary, (int)(dictLength - state.wsize), state.window, 0, (int)state.wsize);
                state.whave = state.wsize;
            }
            else
            {
                //was memcpy(state.window+state.wsize-dictLength, dictionary, dictLength);
                Buffer.BlockCopy(dictionary, 0, state.window, (int)(state.wsize - dictLength), (int)dictLength);
                state.whave = dictLength;
            }
            state.havedict = 1;
            //Tracev((stderr, "inflate:   dictionary set\n"));
            return Z_OK;
        }

        //    inflateGetHeader() requests that gzip header information be stored in the
        // provided gz_header structure.  inflateGetHeader() may be called after
        // inflateInit2() or inflateReset(), and before the first call of inflate().
        // As inflate() processes the gzip stream, head.done is zero until the header
        // is completed, at which time head.done is set to one.  If a zlib stream is
        // being decoded, then head.done is set to -1 to indicate that there will be
        // no gzip header information forthcoming.  Note that Z_BLOCK can be used to
        // force inflate() to return immediately after header processing is complete
        // and before any actual data is decompressed.

        //    The text, time, xflags, and os fields are filled in with the gzip header
        // contents.  hcrc is set to true if there is a header CRC.  (The header CRC
        // was valid if done is set to one.)  If extra is not Z_NULL, then extra_max
        // contains the maximum number of bytes to write to extra.  Once done is true,
        // extra_len contains the actual extra field length, and extra contains the
        // extra field, or that field truncated if extra_max is less than extra_len.
        // If name is not Z_NULL, then up to name_max characters are written there,
        // terminated with a zero unless the length is greater than name_max.  If
        // comment is not Z_NULL, then up to comm_max characters are written there,
        // terminated with a zero unless the length is greater than comm_max.  When
        // any of extra, name, or comment are not Z_NULL and the respective field is
        // not present in the header, then that field is set to Z_NULL to signal its
        // absence.  This allows the use of deflateSetHeader() with the returned
        // structure to duplicate the header.  However if those fields are set to
        // allocated memory, then the application will need to save those pointers
        // elsewhere so that they can be eventually freed.

        //    If inflateGetHeader is not used, then the header information is simply
        // discarded.  The header is always checked for validity, including the header
        // CRC if present.  inflateReset() will reset the process to discard the header
        // information.  The application would need to call inflateGetHeader() again to
        // retrieve the header from the next gzip stream.

        //    inflateGetHeader returns Z_OK if success, or Z_STREAM_ERROR if the source
        // stream state was inconsistent.

        public static int inflateGetHeader(z_stream strm, gz_header head)
        {
            // check state
            if (strm == null || strm.state == null) return Z_STREAM_ERROR;
            inflate_state state = (inflate_state)strm.state;
            if ((state.wrap & 2) == 0) return Z_STREAM_ERROR;

            // save header structure
            state.head = head;
            head.done = 0;
            return Z_OK;
        }

        // Search buf[0..len-1] for the pattern: 0, 0, 0xff, 0xff.  Return when found
        // or when out of input.  When called, *have is the number of pattern bytes
        // found in order so far, in 0..3.  On return *have is updated to the new
        // state.  If on return *have equals four, then the pattern was found and the
        // return value is how many bytes were read including the last byte of the
        // pattern.  If *have is less than four, then the pattern has not been found
        // yet and the return value is len.  In the latter case, syncsearch() can be
        // called again with more data and the *have state.  *have is initialized to
        // zero for the first call.

        private static uint syncsearch(ref uint have, byte[] buf, uint buf_ind, uint len)
        {
            uint got = have;
            uint next = 0;

            while (next < len && got < 4)
            {
                if (buf[buf_ind + next] == (got < 2 ? 0 : 0xff)) got++;
                else if (buf[buf_ind + next] != 0) got = 0;
                else got = 4 - got;
                next++;
            }
            have = got;
            return next;
        }

        //   Skips invalid compressed data until a full flush point (see above the
        // description of deflate with Z_FULL_FLUSH) can be found, or until all
        // available input is skipped. No output is provided.

        //   inflateSync returns Z_OK if a full flush point has been found, Z_BUF_ERROR
        // if no more input was provided, Z_DATA_ERROR if no flush point has been found,
        // or Z_STREAM_ERROR if the stream structure was inconsistent. In the success
        // case, the application may save the current current value of total_in which
        // indicates where valid compressed data was found. In the error case, the
        // application may repeatedly call inflateSync, providing more input each time,
        // until success or end of the input data.

        public static int inflateSync(z_stream strm)
        {
            uint len;                   // number of bytes to look at or looked at
            uint _in, _out;             // temporary to save total_in and total_out
            byte[] buf = new byte[4];       // to restore bit buffer to byte string

            // check parameters
            if (strm == null || strm.state == null) return Z_STREAM_ERROR;
            inflate_state state = (inflate_state)strm.state;
            if (strm.avail_in == 0 && state.bits < 8) return Z_BUF_ERROR;

            // if first time, start search in bit buffer
            if (state.mode != inflate_mode.SYNC)
            {
                state.mode = inflate_mode.SYNC;
                state.hold <<= (int)(state.bits & 7);
                state.bits -= state.bits & 7;
                len = 0;
                while (state.bits >= 8)
                {
                    buf[len++] = (byte)state.hold;
                    state.hold >>= 8;
                    state.bits -= 8;
                }
                state.have = 0;
                syncsearch(ref state.have, buf, 0, len);
            }

            // search available input
            len = syncsearch(ref state.have, strm.in_buf, strm.next_in, strm.avail_in);
            strm.avail_in -= len;
            strm.next_in += len;
            strm.total_in += len;

            // return no joy or set up to restart inflate() on a new block
            if (state.have != 4) return Z_DATA_ERROR;
            _in = strm.total_in; _out = strm.total_out;
            inflateReset(strm);
            strm.total_in = _in; strm.total_out = _out;
            state.mode = inflate_mode.TYPE;
            return Z_OK;
        }

        // Returns true if inflate is currently at the end of a block generated by
        // Z_SYNC_FLUSH or Z_FULL_FLUSH. This function is used by one PPP
        // implementation to provide an additional safety check. PPP uses
        // Z_SYNC_FLUSH but removes the length bytes of the resulting empty stored
        // block. When decompressing, PPP checks that at the end of input packet,
        // inflate is waiting for these length bytes.

        public static int inflateSyncPoint(z_stream strm)
        {
            if (strm == null || strm.state == null) return Z_STREAM_ERROR;
            inflate_state state = (inflate_state)strm.state;
            return (state.mode == inflate_mode.STORED && state.bits == 0) ? 1 : 0;
        }

        //   Sets the destination stream as a complete copy of the source stream.

        //   This function can be useful when randomly accessing a large stream.  The
        // first pass through the stream can periodically record the inflate state,
        // allowing restarting inflate at those points when randomly accessing the
        // stream.

        //   inflateCopy returns Z_OK if success, Z_MEM_ERROR if there was not
        // enough memory, Z_STREAM_ERROR if the source stream state was inconsistent
        // (such as zalloc being NULL). msg is left unchanged in both source and
        // destination.

        public static int inflateCopy(z_stream dest, z_stream source)
        {
            // check input
            if (dest == null || source == null || source.state == null) return Z_STREAM_ERROR;
            inflate_state state = (inflate_state)source.state;

            // allocate space
            inflate_state copy = null;

            try
            {
                copy = state.GetCopy();
            }
            catch (Exception)
            {
                copy = null;
                return Z_MEM_ERROR;
            }

            source.CopyTo(dest); // copy state
            dest.state = copy;
            return Z_OK;
        }

        public static int inflateUndermine(z_stream strm, bool subvert)
        {
            if (strm == null || strm.state == null) return Z_STREAM_ERROR;
            inflate_state state = (inflate_state)strm.state;
            state.sane = !subvert;
#if INFLATE_ALLOW_INVALID_DISTANCE_TOOFAR_ARRR
			return Z_OK;
#else
            state.sane = true;
            return Z_DATA_ERROR;
#endif
        }

        public static long inflateMark(z_stream strm)
        {
            if (strm == null || strm.state == null) return -1L << 16;
            inflate_state state = (inflate_state)strm.state;
            return ((long)(state.back) << 16) +
                (state.mode == inflate_mode.COPY ? state.length : (state.mode == inflate_mode.MATCH ? state.was - state.length : 0));
        }
    }
}
