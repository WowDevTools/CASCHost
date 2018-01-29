// inffast.cs -- fast decoding
// Copyright (C) 1995-2008,2010 Mark Adler
// Copyright (C) 2007-2011 by the Authors
// For conditions of distribution and use, see copyright notice in License.txt

using System;

namespace CASCEdit.ZLib
{
    public static partial class zlib
    {
        // Decode literal, length, and distance codes and write out the resulting
        // literal and match bytes until either not enough input or output is
        // available, an end-of-block is encountered, or a data error is encountered.
        // When large enough input and output buffers are supplied to inflate(), for
        // example, a 16K input buffer and a 64K output buffer, more than 95% of the
        // inflate execution time is spent in this routine.
        //
        // Entry assumptions:
        //
        //      state.mode == LEN
        //      strm.avail_in >= 6
        //      strm.avail_out >= 258
        //      start >= strm.avail_out
        //      state.bits < 8
        //
        // On return, state.mode is one of:
        //
        //      LEN -- ran out of enough output space or enough available input
        //      TYPE -- reached end of block code, inflate() to interpret next block
        //      BAD -- error in block data
        //
        // Notes:
        //
        //  - The maximum input bits used by a length/distance pair is 15 bits for the
        //    length code, 5 bits for the length extra, 15 bits for the distance code,
        //    and 13 bits for the distance extra.  This totals 48 bits, or six bytes.
        //    Therefore if strm.avail_in >= 6, then there is enough input to avoid
        //    checking for available input while decoding.
        //
        //  - The maximum bytes that a single length/distance pair can output is 258
        //    bytes, which is the maximum length that can be coded.  inflate_fast()
        //    requires strm.avail_out >= 258 for each loop to avoid checking for
        //    output space.

        private static void inflate_fast(z_stream strm, uint start) // inflate()'s starting value for strm.avail_out
        {
            inflate_state state = null;
            byte[] _in;             // local strm.next_in
            uint in_ind;            // ind in _in
            int last;               // while in < last, enough input available
            byte[] @out;            // local strm.next_out
            int out_ind;            // ind in @out
            int beg;                // inflate()'s initial strm.next_out
            int end;                // while out < end, enough space available
            uint dmax;              // maximum distance from zlib header
            uint wsize;             // window size or zero if not using window
            uint whave;             // valid bytes in the window
            uint wnext;             // window write index
            byte[] window;          // allocated sliding window, if wsize != 0
            uint hold;              // local strm.hold
            uint bits;              // local strm.bits
            code[] lcode;           // local strm.lencode
            code[] dcode;           // local strm.distcode
            int dcode_ind;          // index in strm.distcode
            uint lmask;             // mask for first level of length codes
            uint dmask;             // mask for first level of distance codes
            code here;              // retrieved table entry
            uint op;                // code bits, operation, extra bits, or window position, window bytes to copy
            uint len;               // match length, unused bytes
            uint dist;              // match distance
            byte[] from;            // where to copy match from
            int from_ind;           // where to copy match from

            // copy state to local variables
            state = (inflate_state)strm.state;
            _in = strm.in_buf;
            in_ind = strm.next_in;
            last = (int)(in_ind + (strm.avail_in - 5));
            @out = strm.out_buf;
            out_ind = strm.next_out;
            beg = (int)(out_ind - (start - strm.avail_out));
            end = (int)(out_ind + (strm.avail_out - 257));
            dmax = state.dmax;
            wsize = state.wsize;
            whave = state.whave;
            wnext = state.wnext;
            window = state.window;
            hold = state.hold;
            bits = state.bits;
            lcode = state.lencode;
            dcode = state.distcode;
            dcode_ind = state.distcode_ind;
            lmask = (1U << (int)state.lenbits) - 1;
            dmask = (1U << (int)state.distbits) - 1;

            // decode literals and length/distances until end-of-block or not enough input data or output space
            do
            {
                if (bits < 15)
                {
                    hold += (uint)_in[in_ind++] << (int)bits;
                    bits += 8;
                    hold += (uint)_in[in_ind++] << (int)bits;
                    bits += 8;
                }
                here = lcode[hold & lmask];

                dolen:
                op = here.bits;
                hold >>= (int)op;
                bits -= op;
                op = here.op;

                if (op == 0)
                { // literal
                  //Tracevv((stderr, @this.val >= 0x20 && @this.val < 0x7f ?
                  //    "inflate:         literal '%c'\n" :
                  //    "inflate:         literal 0x%02x\n", @this.val));
                    @out[out_ind++] = (byte)here.val;
                }
                else if ((op & 16) != 0)
                { // length base
                    len = here.val;
                    op &= 15;                   // number of extra bits
                    if (op != 0)
                    {
                        if (bits < op)
                        {
                            hold += (uint)_in[in_ind++] << (int)bits;
                            bits += 8;
                        }
                        len += (uint)hold & ((1U << (int)op) - 1);
                        hold >>= (int)op;
                        bits -= op;
                    }
                    //Tracevv((stderr, "inflate:         length %u\n", len));

                    if (bits < 15)
                    {
                        hold += (uint)_in[in_ind++] << (int)bits;
                        bits += 8;
                        hold += (uint)_in[in_ind++] << (int)bits;
                        bits += 8;
                    }
                    here = dcode[dcode_ind + (hold & dmask)];

                    dodist:
                    op = here.bits;
                    hold >>= (int)op;
                    bits -= op;
                    op = here.op;
                    if ((op & 16) != 0)
                    { // distance base
                        dist = here.val;
                        op &= 15;               // number of extra bits
                        if (bits < op)
                        {
                            hold += (uint)_in[in_ind++] << (int)bits;
                            bits += 8;
                            if (bits < op)
                            {
                                hold += (uint)_in[in_ind++] << (int)bits;
                                bits += 8;
                            }
                        }
                        dist += (uint)hold & ((1U << (int)op) - 1);
                        if (dist > dmax)
                        {
                            strm.msg = "invalid distance too far back (STRICT)";
                            state.mode = inflate_mode.BAD;
                            break;
                        }
                        hold >>= (int)op;
                        bits -= op;
                        //Tracevv((stderr, "inflate:         distance %u\n", dist));
                        op = (uint)(out_ind - beg);     // max distance in output
                        if (dist > op)
                        { // see if copy from window
                            op = dist - op;             // distance back in window
                            if (op > whave)
                            {
                                if (state.sane)
                                {
                                    strm.msg = "invalid distance too far back";
                                    state.mode = inflate_mode.BAD;
                                    break;
                                }
#if INFLATE_ALLOW_INVALID_DISTANCE_TOOFAR_ARRR
								if(len<=op-whave)
								{
									do
									{
										@out[out_ind++]=0;
									} while(--len!=0);
									continue;
								}
								len-=op-whave;
								do
								{
									@out[out_ind++]=0;
								} while(--op>whave);
								if(op==0)
								{
									from=@out;
									from_ind=(int)(out_ind-dist);
									do
									{
										@out[out_ind++]=from[from_ind++];
									} while(--len!=0);
									continue;
								}
#endif
                            }

                            from = window;
                            from_ind = 0;
                            if (wnext == 0)
                            { // very common case
                                from_ind += (int)(wsize - op);
                                if (op < len)
                                { // some from window
                                    len -= op;
                                    do
                                    {
                                        @out[out_ind++] = from[from_ind++];
                                    } while ((--op) != 0);
                                    from = @out;
                                    from_ind = (int)(out_ind - dist);   // rest from output
                                }
                            }
                            else if (wnext < op)
                            { // wrap around window
                                from_ind += (int)(wsize + wnext - op);
                                op -= wnext;
                                if (op < len)
                                { // some from end of window
                                    len -= op;
                                    do
                                    {
                                        @out[out_ind++] = from[from_ind++];
                                    } while ((--op) != 0);
                                    from = window;
                                    from_ind = 0;
                                    if (wnext < len)
                                    {  // some from start of window
                                        op = wnext;
                                        len -= op;
                                        do
                                        {
                                            @out[out_ind++] = from[from_ind++];
                                        } while ((--op) != 0);
                                        from = @out;
                                        from_ind = (int)(out_ind - dist);   // rest from output
                                    }
                                }
                            }
                            else
                            { // contiguous in window
                                from_ind += (int)(wnext - op);
                                if (op < len)
                                { // some from window
                                    len -= op;
                                    do
                                    {
                                        @out[out_ind++] = from[from_ind++];
                                    } while ((--op) != 0);
                                    from = @out;
                                    from_ind = (int)(out_ind - dist);   // rest from output
                                }
                            }
                            while (len > 2)
                            {
                                @out[out_ind++] = from[from_ind++];
                                @out[out_ind++] = from[from_ind++];
                                @out[out_ind++] = from[from_ind++];
                                len -= 3;
                            }
                            if (len != 0)
                            {
                                @out[out_ind++] = from[from_ind++];
                                if (len > 1)
                                    @out[out_ind++] = from[from_ind++];
                            }
                        }
                        else
                        {
                            from = @out;
                            from_ind = (int)(out_ind - dist);       // copy direct from output
                            do
                            { // minimum length is three
                                @out[out_ind++] = from[from_ind++];
                                @out[out_ind++] = from[from_ind++];
                                @out[out_ind++] = from[from_ind++];
                                len -= 3;
                            } while (len > 2);
                            if (len != 0)
                            {
                                @out[out_ind++] = from[from_ind++];
                                if (len > 1)
                                    @out[out_ind++] = from[from_ind++];
                            }
                        }
                    }
                    else if ((op & 64) == 0)
                    { // 2nd level distance code
                        here = dcode[dcode_ind + here.val + (hold & ((1U << (int)op) - 1))];
                        goto dodist;
                    }
                    else
                    {
                        strm.msg = "invalid distance code";
                        state.mode = inflate_mode.BAD;
                        break;
                    }
                }
                else if ((op & 64) == 0)
                { // 2nd level length code
                    here = lcode[here.val + (hold & ((1U << (int)op) - 1))];
                    goto dolen;
                }
                else if ((op & 32) != 0)
                { // end-of-block
                  //Tracevv((stderr, "inflate:         end of block\n"));
                    state.mode = inflate_mode.TYPE;
                    break;
                }
                else
                {
                    strm.msg = "invalid literal/length code";
                    state.mode = inflate_mode.BAD;
                    break;
                }
            } while (in_ind < last && out_ind < end);

            // return unused bytes (on entry, bits < 8, so in won't go too far back)
            len = bits >> 3;
            in_ind -= len;
            bits -= len << 3;
            hold &= (1U << (int)bits) - 1;

            // update state and return
            strm.next_in = in_ind;
            strm.next_out = out_ind;
            strm.avail_in = (uint)(in_ind < last ? 5 + (last - in_ind) : 5 - (in_ind - last));
            strm.avail_out = (uint)(out_ind < end ? 257 + (end - out_ind) : 257 - (out_ind - end));
            state.hold = hold;
            state.bits = bits;
        }

        // inflate_fast() speedups that turned out slower (on a PowerPC G3 750CXe):
        // - Using bit fields for code structure
        // - Different op definition to avoid & for extra bits (do & for table bits)
        // - Three separate decoding do-loops for direct, window, and wnext == 0
        // - Special case for distance > 1 copies to do overlapped load and store copy
        // - Explicit branch predictions (based on measured branch probabilities)
        // - Deferring match copy and interspersed it with decoding subsequent codes
        // - Swapping literal/length else
        // - Swapping window/direct else
        // - Larger unrolled copy loops (three is about right)
        // - Moving len -= 3 statement into middle of loop
    }
}
