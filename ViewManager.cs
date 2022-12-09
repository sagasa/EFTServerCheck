using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;
using System.ComponentModel;

namespace EFTServerCheck
{

   

    internal class ViewManager
    {
        

        public const ushort FOREGROUND_BLUE = 0x0001; // text color contains blue.
        public const ushort FOREGROUND_GREEN = 0x0002; // text color contains green.
        public const ushort FOREGROUND_RED = 0x0004; // text color contains red.

        public const ushort FOREGROUND_CYAN = FOREGROUND_GREEN | FOREGROUND_RED;
        public const ushort FOREGROUND_MAGENTA = FOREGROUND_RED | FOREGROUND_BLUE;
        public const ushort FOREGROUND_YELLOW = FOREGROUND_RED | FOREGROUND_GREEN;

        public const ushort FOREGROUND_WHITE = FOREGROUND_GREEN | FOREGROUND_BLUE | FOREGROUND_RED;
        public const ushort FOREGROUND_BLACK = 0;

        public const ushort FOREGROUND_GRAY = 0x0008;

        public const ushort FOREGROUND_DARKBLUE = FOREGROUND_BLUE | FOREGROUND_GRAY; 
        public const ushort FOREGROUND_DARKGREEN = FOREGROUND_GREEN | FOREGROUND_GRAY; 
        public const ushort FOREGROUND_DARKRED = FOREGROUND_RED | FOREGROUND_GRAY; 

        public const ushort FOREGROUND_DARKCYAN = FOREGROUND_GREEN | FOREGROUND_RED | FOREGROUND_GRAY;
        public const ushort FOREGROUND_DARKMAGENTA = FOREGROUND_RED | FOREGROUND_BLUE | FOREGROUND_GRAY;
        public const ushort FOREGROUND_DARKYELLOW = FOREGROUND_RED | FOREGROUND_GREEN | FOREGROUND_GRAY;

        



        public const ushort BACKGROUND_BLUE = 0x0010; // background color contains blue.
        public const ushort BACKGROUND_GREEN = 0x0020;// background color contains green.
        public const ushort BACKGROUND_RED = 0x0040;// background color contains red.
        public const ushort BACKGROUND_INTENSITY = 0x0080; // background color is intensified.

        /*
        Black = 0,
        DarkBlue = 1,
        DarkGreen = 2,
        DarkCyan = 3,
        DarkRed = 4,
        DarkMagenta = 5,
        DarkYellow = 6,
        Gray = 7,
        DarkGray = 8,
        Blue = 9,
        Green = 10,
        Cyan = 11,
        Red = 12,
        Magenta = 13,
        Yellow = 14,
        White = 15
        //*/

        private const int CONSOLE_APPEND_SIZE = 100;


        private static readonly IntPtr Handle = GetConsoleHandle();

        private static ReaderWriterLockSlim _locker = new(LockRecursionPolicy.SupportsRecursion);

        private static SortedList<int, LineEntry> _lines = new();


        [DllImport("kernel32.dll",SetLastError =true)]
        static extern bool WriteConsoleOutputCharacter(
            IntPtr hConsoleOutput,
            byte[] lpCharacter,
            int nLength,
            COORD dwWriteCoord,
            out int lpNumberOfCharsWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteConsoleOutputAttribute(
            IntPtr hConsoleOutput,
            ushort[] lpAttribute,
            int nLength,
            COORD dwWriteCoord,
            out int lpNumberOfAttrsWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetConsoleCursorPosition(
            IntPtr hConsoleOutput,
            COORD dwWriteCoord);
        

        struct COORD
        {
            public COORD(int x, int y)
            {
                X = (short)x;
                Y = (short)y;
            }
            internal short X;
            internal short Y;
        }


        private static IntPtr GetConsoleHandle()
        {
            var consoleType = typeof(Console);
            var consolePalType = consoleType.Assembly.GetType("System.ConsolePal");
            var outputHandleProp = consolePalType.GetProperty("OutputHandle", BindingFlags.Static | BindingFlags.NonPublic);
            return (IntPtr)outputHandleProp.GetValue(null);
        }


        private static byte[] FormatText(object? obj)
        {
            string text = obj?.ToString() ?? "";
            return Console.OutputEncoding.GetBytes(text[..Math.Min(Console.BufferWidth, text.Length)].PadRight(Console.BufferWidth));
        }

        /// <summary>
        /// テキストを追加
        /// 自動改行無し
        /// </summary>
        public static ILine PrintLine(object? obj = null, int? pos = null, ushort color = FOREGROUND_WHITE)
        {
            _locker.EnterWriteLock();
            try
            {
                int p = pos ?? (_lines.Count == 0 ? 0 : _lines.Last().Key + 1);
                if (Console.BufferHeight - 1 <= p)
                {
                    Console.BufferHeight += CONSOLE_APPEND_SIZE;
                }
                var line = AddLine(p);

                SetLine(line, obj, color);
                SetConsoleCursorPosition(Handle, new COORD(0,p));
                return line;
            }
            finally
            {
                _locker.ExitWriteLock();
            }
        }

        private static void SetLine(LineEntry line, object? obj, ushort color = FOREGROUND_WHITE)
        {
            var text = FormatText(obj);
            _locker.EnterWriteLock();
            try
            {
                var pos = line.Pos;
                //text = FormatText((obj?.ToString() ?? "") + " " + pos);

                var coord = new COORD(0, (short)pos);

                ushort[] attr = new ushort[text.Length];
                Array.Fill<ushort>(attr, color);
                int res;
                if (!WriteConsoleOutputAttribute(Handle, attr, text.Length, coord, out res))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!WriteConsoleOutputCharacter(Handle, text, text.Length, coord, out res))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                _locker.ExitWriteLock();
            }
        }



        //startより下をdeff行下に動かす
        public static void Move(int start, int def)
        {
            if (_lines.Count == 0) return;
            var topIndex = BinarySearch(_lines.Keys, start);
            int top = _lines.Keys[topIndex];
            var bottom = _lines.Keys.Last();

            var list = new List<LineEntry>();
            //移動元を除去
            for (int i = _lines.Count - 1; i >= topIndex; i--)
            {
                list.Add(_lines.Values[i]);
                _lines.RemoveAt(i);
            }
            //再登録
            list.ForEach(line =>
            {
                line.Pos += def;
                _lines[line.Pos] = line;
            });
            Console.MoveBufferArea(0, top, Console.BufferWidth, bottom - top + 1, 0, top + def);
        }

        //等しい要素のindex 無ければ以上で最小の要素のindex
        static int BinarySearch<T>(IList<T> list, T target) where T : IComparable<T>
        {
            var max = list.Count;
            var min = -1;



            while (Math.Abs(max - min) > 1)
            {
                var mid = (max + min) / 2;
                var comp = target.CompareTo(list[mid]);
                if (comp == 0)
                    return mid;
                else if (comp < 0)
                {
                    max = mid;
                }
                else
                {
                    min = mid;
                }
            }
            return max;
        }

        private static LineEntry AddLine(int pos)
        {
            if (_lines.ContainsKey(pos))
                _lines[pos].Pos = -1;

            var line = new LineEntry(pos);
            _lines[pos] = line;
            return line;
        }
        private static void RemoveLine(int pos)
        {
            if (_lines.ContainsKey(pos))
            {
                var line = _lines[pos];
                line.Pos = -1;
                _lines.Remove(pos);
                Move(pos + 1, -1);
            }

        }

        public interface ILine
        {
            void Delete();
            void Replace(object? obj = null, ushort color = FOREGROUND_WHITE);
        }

        class LineEntry : ILine
        {
            public int Pos;
            public string? Line;
            public ConsoleColor? Color;
            public LineEntry(int pos)
            {
                Pos = pos;
            }

            public void Delete()
            {
                _locker.EnterWriteLock();
                try
                {
                    if (!Valid())
                        return;

                    RemoveLine(Pos);
                }
                finally
                {
                    _locker.ExitWriteLock();
                }

            }

            public void Replace(object? obj = null, ushort color = FOREGROUND_WHITE)
            {
                _locker.EnterWriteLock();
                if (Valid())
                    SetLine(this, obj, color);
                _locker.ExitWriteLock();
            }

            private bool Valid() => Pos != -1;
        }
    }
}
