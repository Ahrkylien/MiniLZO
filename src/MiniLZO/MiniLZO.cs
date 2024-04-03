#region Copyright notice
/*
This is a C# conversion of the C library called miniLZO.
This C# implementation is created by Henk van Grootheest and is based on the C# port by Frank Razenberg:
https://github.com/zzattack/MiniLZO

LZO and miniLZO are Copyright (C) 1996-2017 Markus Franz Xaver Oberhumer
All Rights Reserved.

LZO and miniLZO are distributed under the terms of the GNU General
Public License (GPL).

http://www.oberhumer.com/opensource/lzo/
*/
#endregion

using System;

namespace MiniLZO
{
    public static class MiniLZO
    {
        public static byte[] Decompress(byte[] compressed, int uncompressedLength)
        {
            byte[] decompressed = new byte[uncompressedLength];
            var lzoDecompressor = new Lzo1xDecompressor(compressed, decompressed);
            var result = lzoDecompressor.Decompress();
            if (result != 0)
                throw new Exception("Decompression failed.");
            return decompressed;
        }

        public static byte[] Compress(byte[] uncompressed)
        {
            byte[] compressed = new byte[uncompressed.Length + (uncompressed.Length / 16) + 64 + 3];
            var lzoCompressor = new Lzo1xCompressor(uncompressed, compressed);
            var outputLength = lzoCompressor.Compress();
            Array.Resize(ref compressed, (int)outputLength);
            return compressed;
        }
    }

    public class Lzo1xCompressor
    {
        private uint _inputPointer = 0;
        private uint _outputPointer = 0;

        private readonly byte[] _input;
        private readonly byte[] _output;

        private readonly ushort[] _workMemory = new ushort[IntPtr.Size * 8192];

        private byte InputByte => _input[_inputPointer++];
        private byte InputBytePeek => _input[_inputPointer];
        private byte OutputByte { set { _output[_outputPointer++] = value; } }

        public Lzo1xCompressor(byte[] input, byte[] output)
        {
            _input = input;
            _output = output;
            for (var i = 0; i < _output.Length; i++)
                _output[i] = 0xFF;
        }

        public uint Compress()
        {
            uint lengthLeftToRead = (uint)_input.Length;
            uint t = 0;

            uint outputPointer = 0; // Quick fix

            while (lengthLeftToRead > 20)
            {
                uint lengthToReadThisCycle = lengthLeftToRead;
                lengthToReadThisCycle = Math.Min(lengthToReadThisCycle, 0xC000);
                ulong inputPointerAfterCycle = (ulong)_inputPointer + lengthToReadThisCycle;
                if ((inputPointerAfterCycle + ((t + lengthToReadThisCycle) >> 5)) <= inputPointerAfterCycle
                    || (inputPointerAfterCycle + ((t + lengthToReadThisCycle) >> 5)) <= (_inputPointer + lengthToReadThisCycle))
                {
                    break;
                }

                Array.Clear(_workMemory, 0, _workMemory.Length);

                var inputPointerBefore = _inputPointer; // Quick fix
                _outputPointer = outputPointer; // Quick fix
                var result = Lzo1x1CompressCore(lengthToReadThisCycle, t);
                t = result.Item1;
                var outLen = result.Item2;
                _inputPointer = inputPointerBefore + lengthToReadThisCycle; // Quick fix
                outputPointer += outLen; // Quick fix
                lengthLeftToRead -= lengthToReadThisCycle;
            }
            _outputPointer = outputPointer; // Quick fix
            t += lengthLeftToRead;
            if (t > 0)
            {
                WriteLength(t, isLastWrite: true);
                CopyBytesFromPointer((uint)(_input.Length - t), t);
            }
            OutputByte = 16 | 1;
            OutputByte = 0;
            OutputByte = 0;
            return _outputPointer;
        }

        private (uint, uint) Lzo1x1CompressCore(uint inputLength, uint ti)
        {
            uint outputPointerStart = _outputPointer;
            uint inputPointerStart = _inputPointer;
            uint inputPointerEndHmm = _inputPointer + inputLength;
            uint inputPointerEnd = _inputPointer + inputLength - 20;
            uint inputPointerTemp = _inputPointer;
            _inputPointer += ti < 4 ? 4 - ti : 0;

            uint inputPointerM;
            uint memoryOffset;
            uint memoryLength;

            var incrementInputPointerAtStartOfLoop = true;
            while (true)
            {
                if (incrementInputPointerAtStartOfLoop)
                    _inputPointer += 1 + ((_inputPointer - inputPointerTemp) >> 5);

                if (_inputPointer >= inputPointerEnd)
                    break;

                uint dv = ReadUintFromInput(_inputPointer);
                uint workMemoryIndex = (0x1824429d * dv >> (32 - 14)) & ((1u << 14) - 1);
                inputPointerM = inputPointerStart + _workMemory[workMemoryIndex];
                _workMemory[workMemoryIndex] = (ushort)(_inputPointer - inputPointerStart);
                if (dv != ReadUintFromInput(inputPointerM))
                {
                    incrementInputPointerAtStartOfLoop = true;
                    continue;
                }

                inputPointerTemp -= ti;
                ti = 0;
                uint t = _inputPointer - inputPointerTemp;
                if (t != 0)
                {
                    WriteLength(t);
                    CopyBytesFromPointer(inputPointerTemp, t);
                }
                memoryLength = 4;
                while (true)
                {
                    uint v = ReadUintFromInput(_inputPointer + memoryLength) ^ ReadUintFromInput(inputPointerM + memoryLength);
                    if (v != 0)
                    {
                        memoryLength += (uint)TrailingZeroCountUint32(v) / 8;
                        break;
                    }
                    memoryLength += 4;
                    if (_inputPointer + memoryLength >= inputPointerEnd)
                        break;
                }
                // memoryLength done calculating:
                memoryOffset = _inputPointer - inputPointerM;
                _inputPointer += memoryLength;
                inputPointerTemp = _inputPointer;
                if (memoryLength <= 8 && memoryOffset <= 0x0800)
                {
                    memoryOffset -= 1;
                    OutputByte = (byte)(((memoryLength - 1) << 5) | ((memoryOffset & 7) << 2));
                    OutputByte = (byte)(memoryOffset >> 3);
                }
                else if (memoryOffset <= 0x4000)
                {
                    memoryOffset -= 1;
                    if (memoryLength <= 33)
                        OutputByte = (byte)(32 | (memoryLength - 2));
                    else
                    {
                        memoryLength -= 33;
                        OutputByte = 32;
                        WriteLengthLong(memoryLength);
                    }
                    OutputByte = (byte)(memoryOffset << 2);
                    OutputByte = (byte)(memoryOffset >> 6);
                }
                else
                {
                    memoryOffset -= 0x4000;
                    if (memoryLength <= 9)
                        OutputByte = (byte)(16 | ((memoryOffset >> 11) & 8) | (memoryLength - 2));
                    else
                    {
                        memoryLength -= 9;
                        OutputByte = (byte)(16 | ((memoryOffset >> 11) & 8)); // 224 is faulty set to 16, should be 32
                        WriteLengthLong(memoryLength);
                    }
                    OutputByte = (byte)(memoryOffset << 2);
                    OutputByte = (byte)(memoryOffset >> 6);
                }
                incrementInputPointerAtStartOfLoop = false;
            }
            return (inputPointerEndHmm - (inputPointerTemp - ti), _outputPointer - outputPointerStart);
        }

        private void WriteLength(uint length, bool isLastWrite = false)
        {
            if (isLastWrite && _outputPointer == 0 && length <= 238)
                OutputByte = (byte)(17 + length);
            else if (length <= 3)
                _output[_outputPointer - 2] |= (byte)length;
            else if (length <= 18)
                OutputByte = (byte)(length - 3);
            else
            {
                length -= 18;
                OutputByte = 0;
                WriteLengthLong(length);
            }
        }

        private void WriteLengthLong(uint length)
        {
            while (length > 255)
            {
                OutputByte = 0;
                length -= 255;
            }
            OutputByte = (byte)length;
        }

        private void CopyBytesFromPointer(uint customInputPointer, uint numberOfBytes)
        {
            for (int i = 0; i < numberOfBytes; i++)
                _output[_outputPointer + i] = _input[customInputPointer + i];
            _outputPointer += numberOfBytes;
        }

        private uint ReadUintFromInput(uint index)
        {
            return (uint)(_input[index] + (_input[index + 1] << 8) + (_input[index + 2] << 16) + (_input[index + 3] << 24));
        }

        static readonly int[] MultiplyDeBruijnBitPosition = {
                  0, 1, 28, 2, 29, 14, 24, 3, 30, 22, 20, 15, 25, 17, 4, 8,
                  31, 27, 13, 23, 21, 19, 16, 7, 26, 12, 18, 6, 11, 5, 10, 9
                };

        private static int TrailingZeroCountUint32(uint v)
        {
            return MultiplyDeBruijnBitPosition[((uint)((v & -v) * 0x077CB531U)) >> 27];
        }
    }

    public class Lzo1xDecompressor
    {
        private uint _inputPointer = 0;
        private uint _outputPointer = 0;
        private uint _mOutputPointer;

        private readonly byte[] _input;
        private readonly byte[] _output;

        private byte InputByte => _input[_inputPointer++];
        private byte InputBytePeek => _input[_inputPointer];
        private byte OutputByte { set { _output[_outputPointer++] = value; } }

        public Lzo1xDecompressor(byte[] input, byte[] output)
        {
            _input = input;
            _output = output;
        }

        public int Decompress()
        {
            uint t;
            bool goToFirstLiteralRun = false;
            bool goToMatchdone = false;

            if (InputBytePeek > 17)
            {
                t = (uint)(InputByte - 17);
                if (t >= 4)
                    goToFirstLiteralRun = true;
                CopyBytes(Math.Max(1, t));
            }

            while (true)
            {
                if (goToFirstLiteralRun)
                {
                    goToFirstLiteralRun = false;
                    goto first_literal_run;
                }

                t = InputByte;
                if (t >= 16)
                    goto match;

                if (t == 0)
                    t += 15 + ReadLength();

                CopyBytes(4 + t - 1);

            first_literal_run:
                t = InputByte;
                if (t >= 16)
                    goto match;
                _mOutputPointer = _outputPointer - (1 + 0x0800) - (t >> 2) - ((uint)InputByte << 2);

                OutputByte = _output[_mOutputPointer++];
                OutputByte = _output[_mOutputPointer++];
                OutputByte = _output[_mOutputPointer];
                goToMatchdone = true;

            match:
                while (true)
                {
                    if (goToMatchdone)
                    {
                        goToMatchdone = false;
                        goto match_done;
                    }
                    if (t >= 64)
                    {
                        _mOutputPointer = _outputPointer - 1;
                        _mOutputPointer -= (t >> 2) & 7;
                        _mOutputPointer -= (uint)(InputByte << 3);
                        t = (t >> 5) - 1;

                        CopyBytes(Math.Max(3, t + 2), copyFromOutputBuffer: true);

                        goto match_done;
                    }
                    else if (t >= 32)
                    {
                        t &= 31;
                        if (t == 0)
                            t += 31 + ReadLength();
                        _mOutputPointer = _outputPointer - 1 - (((uint)ReadUshortFromInput()) >> 2);
                        _inputPointer += 2;
                    }
                    else if (t >= 16)
                    {
                        t &= 7;
                        if (t == 0)
                            t += 7 + ReadLength();
                        _mOutputPointer = _outputPointer - ((t & 8) << 11) - ((uint)ReadUshortFromInput() >> 2);
                        _inputPointer += 2;
                        if (_mOutputPointer == _outputPointer)
                            goto eof_found;
                        _mOutputPointer -= 0x4000;
                    }
                    else
                    {
                        _mOutputPointer = _outputPointer - 1 - (t >> 2) - ((uint)InputByte << 2);
                        OutputByte = _output[_mOutputPointer++];
                        OutputByte = _output[_mOutputPointer];
                        goto match_done;
                    }

                    if (t >= 2 * 4 - (3 - 1) && (_outputPointer - _mOutputPointer) >= 4)
                    {
                        t += 4 - (3 - 1);
                        CopyBytes(t, copyFromOutputBuffer: true);
                    }
                    else
                    {
                        CopyBytes(Math.Max(3, t + 2), copyFromOutputBuffer: true);
                    }
                match_done:
                    t = (uint)(_input[_inputPointer - 2] & 3);
                    if (t == 0)
                        break;
                    // Match next:
                    CopyBytes(t);
                    t = InputByte;
                }
            }
        eof_found:
            return (_inputPointer == _input.Length ? 0 : (_inputPointer < _input.Length ? (-8) : (-4)));
        }

        private uint ReadLength()
        {
            uint length = 0;
            while (true)
            {
                var inputByte = InputByte;
                if (inputByte == 0)
                {
                    length += 255;
                }
                else
                {
                    length += inputByte;
                    break;
                }
            }
            return length;
        }

        private void CopyBytes(uint numberOfBytes, bool copyFromOutputBuffer = false)
        {
            if (copyFromOutputBuffer)
            {
                for (int i = 0; i < numberOfBytes; i++)
                    _output[_outputPointer + i] = _output[_mOutputPointer + i];
                _outputPointer += numberOfBytes;
                _mOutputPointer += numberOfBytes;
            }
            else
            {
                for (int i = 0; i < numberOfBytes; i++)
                    _output[_outputPointer + i] = _input[_inputPointer + i];
                _outputPointer += numberOfBytes;
                _inputPointer += numberOfBytes;
            }
        }

        private ushort ReadUshortFromInput()
        {
            return (ushort)(_input[_inputPointer] + (_input[_inputPointer + 1] << 8));
        }
    }
}
