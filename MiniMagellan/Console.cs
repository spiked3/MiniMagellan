using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MiniMagellan
{
    public static class xCon
    {
        public static Object consoleLock = new Object();

        static ConsoleColor FONT_COLOR = Console.ForegroundColor;
        static ConsoleColor BACKGROUND_COLOR = Console.BackgroundColor;

        public static void WriteLine(string v, object o1 = null, object o2 = null, object o3 = null)
        {
            Write(v + o1 + o2 + o3 + '\n');
        }

        public static int Write(string input)
        {
            int i = 0, StringSize = 0;
            lock (consoleLock)
            {
                //if (string.IsNullOrWhiteSpace(input))
                //    return 0;

                ConsoleColor Fore = Console.ForegroundColor;
                ConsoleColor Back = Console.BackgroundColor;

                string pattern = @"[\\]{0,1}(?:[\^|\*]{1})(?:[0-9A-F]{3}|[0-9]{1,2}|[a-zA-Z!\.]{1})";

                MatchCollection matches = Regex.Matches(input, pattern);
                string[] substrings = Regex.Split(input, pattern);

                foreach (string sub in substrings)
                {
                    StringSize += sub.Count();
                    Console.Write(sub);

                    if (i < matches.Count)
                    {
                        char Type = matches[i].Groups[0].Value[0];
                        switch (Type)
                        {
                            default:
                                Console.Write("{0}", matches[i].Groups[0].Value.TrimStart('\\'));
                                break;
                            case '*':
                                Console.BackgroundColor = getColor(matches[i].Groups[0].Value);
                                break;
                            case '^':
                                Console.ForegroundColor = getColor(matches[i].Groups[0].Value);
                                break;
                        }
                    }
                    ++i;
                }
                Console.BackgroundColor = Back;
                Console.ForegroundColor = Fore;
            }

            return StringSize;
        }

        private static ConsoleColor getColor(string s, ConsoleColor? ForeC = null, ConsoleColor? BackC = null)
        {
            ConsoleColor FC = ForeC ?? FONT_COLOR;
            ConsoleColor BC = BackC ?? BACKGROUND_COLOR;

            if (string.IsNullOrWhiteSpace(s)) return FC;

            char Type = s[0];
            s = s.TrimStart(new char[] { '^', '*' });
            int i = -1;

            if (s.Length == 1 || s.Length == 2)
            {
#if !DEBUG
				try
				{
#endif
                // INT CASE
                if (int.TryParse(s, out i))
                    return (ConsoleColor)((i < 0 || i > 16) ? 1 : i);
                else
                {
                    // Char case
                    switch (s.ToLower())
                    {
                        case "!":
                        case "-":// Restore color
                            return (Type == '^') ? FC : BC;
                        case "w":
                            return ConsoleColor.White;
                        case "z":
                            return ConsoleColor.Black;
                        case "y":
                            return ConsoleColor.Yellow;
                        case "g":
                            return ConsoleColor.Green;
                        case "r":
                            return ConsoleColor.Red;
                        case "b":
                            return ConsoleColor.Blue;
                        case "c":
                            return ConsoleColor.Cyan;
                        case "m":
                            return ConsoleColor.Magenta;

                        default:
                            break;
                    }
                }
#if !DEBUG
				}
				catch (Exception e)
				{
					sConsole.WriteLine("\r\n^mxConsole^!: ^rColor Parsing Error! :/ ^y{0}^!\r\nPlease report at: ^boverpowered.it^!\r\n", e.Message);
				}
#endif
            }

            return Console.ForegroundColor;
        }
    }
}