using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EFTServerCheck
{
    internal class ViewManager
    {
        private static int _maxLine = -1;
        private static Mutex _mutex = new Mutex();
        private static List<LineEntry?> _entris = new (new LineEntry[Console.BufferHeight]);
        /// <summary>
        /// テキストを追加
        /// 自動改行無し
        /// </summary>
        public static LineEntry PrintLine(object? obj = null, int? top = null, ConsoleColor color = ConsoleColor.White)
        {
            string text = obj?.ToString()??"";
            text = text[..Math.Min(Console.BufferWidth,text.Length)];
            _mutex.WaitOne();
            Console.ForegroundColor = color;
            Console.SetCursorPosition(0, top ?? _maxLine + 1);
            var pos = Console.GetCursorPosition().Top;
            Console.WriteLine(text);
            _mutex.ReleaseMutex();
            return new LineEntry(pos);
        }
        



        public class LineEntry
        {
            int _pos;
            public LineEntry(int pos)
            {
                _pos = pos;
                _entris[pos] = this;
                _maxLine = Math.Max(_maxLine, pos);
            }

            public void Delete()
            {
                Check();
                Console.MoveBufferArea(0, _pos + 1, Console.BufferWidth, _maxLine - _pos, 0, _pos);
                _mutex.WaitOne();
                _entris.RemoveAt(_pos);
                _entris.Add(null);
                for (int i = _pos; i < _maxLine; i++)
                {
                    if(_entris[i]!=null)
                        _entris[i]!._pos--;
                }
                //maxline更新
                if (_pos == _maxLine)
                {
                    for (int i = _maxLine - 1; i >= 0; i--) {
                        if (_entris[i] != null)
                        {
                            _maxLine = i;
                            break;
                        }
                        //１行もないなら
                        if(i == 0)
                        {
                            _maxLine = -1;
                        }
                     }

                }
                else
                {
                    _maxLine--;
                }
                _mutex.ReleaseMutex();
                _pos = -1;
            }

            public void Replace(object? obj = null, ConsoleColor color = ConsoleColor.White)
            {
                Check();
                PrintLine(obj, _pos,color);
            }

            private void Check()
            {
                if (_pos == -1)
                
                    throw new InvalidOperationException("line was deleted");
            }
        }
    }
}
