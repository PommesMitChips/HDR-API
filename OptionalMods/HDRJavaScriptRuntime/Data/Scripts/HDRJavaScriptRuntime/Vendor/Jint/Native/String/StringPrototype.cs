using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jint.Native.Array;
using Jint.Native.Function;
using Jint.Native.Object;
using Jint.Native.RegExp;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Jint.Native.String
{


    /// <summary>
    /// http://www.ecma-international.org/ecma-262/5.1/#sec-15.5.4
    /// </summary>
    public sealed class StringPrototype : StringInstance
    {
        private StringPrototype(Engine engine)
            : base(engine)
        {
        }

        public static StringPrototype CreatePrototypeObject(Engine engine, StringConstructor stringConstructor)
        {
            var obj = new StringPrototype(engine);
            obj.Prototype = engine.Object.PrototypeObject;
            obj.PrimitiveValue = "";
            obj.Extensible = true;
            obj.FastAddProperty("length", 0, false, false, false); 
            obj.FastAddProperty("constructor", stringConstructor, true, false, true);

            return obj;
        }

        public void Configure()
        {
            FastAddProperty("toString", new ClrFunctionInstance(Engine, ToStringString), true, false, true);
            FastAddProperty("valueOf", new ClrFunctionInstance(Engine, ValueOf), true, false, true);
            FastAddProperty("charAt", new ClrFunctionInstance(Engine, CharAt, 1), true, false, true);
            FastAddProperty("charCodeAt", new ClrFunctionInstance(Engine, CharCodeAt, 1), true, false, true);
            FastAddProperty("concat", new ClrFunctionInstance(Engine, Concat, 1), true, false, true);
            FastAddProperty("indexOf", new ClrFunctionInstance(Engine, IndexOf, 1), true, false, true);
            FastAddProperty("lastIndexOf", new ClrFunctionInstance(Engine, LastIndexOf, 1), true, false, true);
            FastAddProperty("localeCompare", new ClrFunctionInstance(Engine, LocaleCompare, 1), true, false, true);
            FastAddProperty("match", new ClrFunctionInstance(Engine, Match, 1), true, false, true);
            FastAddProperty("replace", new ClrFunctionInstance(Engine, Replace, 2), true, false, true);
            FastAddProperty("search", new ClrFunctionInstance(Engine, Search, 1), true, false, true);
            FastAddProperty("slice", new ClrFunctionInstance(Engine, Slice, 2), true, false, true);
            FastAddProperty("split", new ClrFunctionInstance(Engine, Split, 2), true, false, true);
            FastAddProperty("substr", new ClrFunctionInstance(Engine, Substr, 2), true, false, true);
            FastAddProperty("substring", new ClrFunctionInstance(Engine, Substring, 2), true, false, true);
            FastAddProperty("toLowerCase", new ClrFunctionInstance(Engine, ToLowerCase), true, false, true);
            FastAddProperty("toLocaleLowerCase", new ClrFunctionInstance(Engine, ToLocaleLowerCase), true, false, true);
            FastAddProperty("toUpperCase", new ClrFunctionInstance(Engine, ToUpperCase), true, false, true);
            FastAddProperty("toLocaleUpperCase", new ClrFunctionInstance(Engine, ToLocaleUpperCase), true, false, true);
            FastAddProperty("trim", new ClrFunctionInstance(Engine, Trim), true, false, true);
        }

        private JsValue ToStringString(JsValue thisObj, JsValue[] arguments)
        {
            var s = TypeConverter.ToObject(Engine, thisObj) as StringInstance;
            if (s == null)
            {
                throw new JavaScriptException(Engine.TypeError);
            }

            return s.PrimitiveValue;
        }

        // http://msdn.microsoft.com/en-us/library/system.char.iswhitespace(v=vs.110).aspx
        // http://en.wikipedia.org/wiki/Byte_order_mark
        const char BOM_CHAR = '\uFEFF';
        const char MONGOLIAN_VOWEL_SEPARATOR = '\u180E';

        private static bool IsWhiteSpaceEx(char c)
        {
            return 
                char.IsWhiteSpace(c) || 
                c == BOM_CHAR ||
                // In .NET 4.6 this was removed from WS based on Unicode 6.3 changes
                c == MONGOLIAN_VOWEL_SEPARATOR;
        }

        public static string TrimEndEx(string s)
        {
            if (s.Length == 0)
                return string.Empty;

            var i = s.Length - 1;
            while (i >= 0)
            {
                HdrExecutionGuard.Step();
                if (IsWhiteSpaceEx(s[i]))
                    i--;
                else
                    break;
            }
            if (i >= 0)
                return s.Substring(0, i + 1);
            else
                return string.Empty;
        }

        public static string TrimStartEx(string s)
        {
            if (s.Length == 0)
                return string.Empty;

            var i = 0;
            while (i < s.Length)
            {
                HdrExecutionGuard.Step();
                if (IsWhiteSpaceEx(s[i]))
                    i++;
                else
                    break;
            }
            if (i >= s.Length)
                return string.Empty;
            else
                return s.Substring(i);
        }

        public static string TrimEx(string s)
        {
            return TrimEndEx(TrimStartEx(s));
        } 

        private JsValue Trim(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);
            var s = TypeConverter.ToString(thisObj);
            return TrimEx(s);
        }
        
        private static JsValue ToLocaleUpperCase(JsValue thisObj, JsValue[] arguments)
        {
            var s = TypeConverter.ToString(thisObj);
            return s.ToUpper();
        }

        private static JsValue ToUpperCase(JsValue thisObj, JsValue[] arguments)
        {
            var s = TypeConverter.ToString(thisObj);
            return s.ToUpperInvariant();
        }

        private static JsValue ToLocaleLowerCase(JsValue thisObj, JsValue[] arguments)
        {
            var s = TypeConverter.ToString(thisObj);
            return s.ToLower();
        }

        private static JsValue ToLowerCase(JsValue thisObj, JsValue[] arguments)
        {
            var s = TypeConverter.ToString(thisObj);
            return s.ToLowerInvariant();
        }

        private static int ToIntegerSupportInfinity(JsValue numberVal)
        {
            var doubleVal = TypeConverter.ToInteger(numberVal);
            var intVal = (int) doubleVal;
            if (double.IsPositiveInfinity(doubleVal))
                intVal = int.MaxValue;
            else if (double.IsNegativeInfinity(doubleVal))
                intVal = int.MinValue;
            else
                intVal = (int) doubleVal;
            return intVal;
        }

        private JsValue Substring(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);

            var s = TypeConverter.ToString(thisObj);
            var start = TypeConverter.ToNumber(arguments.At(0));
            var end = TypeConverter.ToNumber(arguments.At(1));

            if (double.IsNaN(start) || start < 0)
            {
                start = 0;
            }

            if (double.IsNaN(end) || end < 0)
            {
                end = 0;
            }

            var len = s.Length;
            var intStart = ToIntegerSupportInfinity(start);

            var intEnd = arguments.At(1) == Undefined.Instance ? len : (int)ToIntegerSupportInfinity(end);
            var finalStart = System.Math.Min(len, System.Math.Max(intStart, 0));
            var finalEnd = System.Math.Min(len, System.Math.Max(intEnd, 0));
            // Swap value if finalStart < finalEnd
            var from = System.Math.Min(finalStart, finalEnd);
            var to = System.Math.Max(finalStart, finalEnd);
            return s.Substring(from, to - from);
        }

        private JsValue Substr(JsValue thisObj, JsValue[] arguments)
        {
            var s = TypeConverter.ToString(thisObj);
            var start = TypeConverter.ToInteger(arguments.At(0));
            var length = arguments.At(1) == JsValue.Undefined 
                ? double.PositiveInfinity 
                : TypeConverter.ToInteger(arguments.At(1));

            start = start >= 0 ? start : System.Math.Max(s.Length + start, 0);
            length = System.Math.Min(System.Math.Max(length, 0), s.Length - start);
            if (length <= 0)
            {
                return "";
            }

            return s.Substring(TypeConverter.ToInt32(start), TypeConverter.ToInt32(length));
        }

        private JsValue Split(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine,thisObj);
            var source=TypeConverter.ToString(thisObj);
            var separator=arguments.At(0);
            var limit=arguments.At(1)==Undefined.Instance ? UInt32.MaxValue : TypeConverter.ToUint32(arguments.At(1));
            var result=(ArrayInstance)Engine.Array.Construct(Arguments.Empty);
            if (limit==0) return result;
            if (separator==Undefined.Instance) return Engine.Array.Construct(Arguments.From(source));
            if (separator.IsRegExp()) throw new System.InvalidOperationException("HDR JavaScript: regular-expression splitting is unavailable.");
            var split=TypeConverter.ToString(separator);
            uint count=0;
            if (split.Length==0)
            {
                for (var i=0; i<source.Length && count<limit; i++)
                {
                    HdrExecutionGuard.Step(); Engine.HdrCheckArray((long)count+1);
                    result.DefineOwnProperty((count++).ToString(),new PropertyDescriptor(source.Substring(i,1),true,true,true),false);
                }
                return result;
            }
            var start=0;
            while (count<limit)
            {
                HdrExecutionGuard.Step();
                var end=source.IndexOf(split,start,System.StringComparison.Ordinal);
                Engine.HdrCheckArray((long)count+1);
                result.DefineOwnProperty((count++).ToString(),new PropertyDescriptor(
                    end<0 ? source.Substring(start) : source.Substring(start,end-start),true,true,true),false);
                if (end<0) break;
                start=end+split.Length;
            }
            return result;
        }

        private JsValue Slice(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);

            var s = TypeConverter.ToString(thisObj);

            var start = TypeConverter.ToNumber(arguments.At(0));
            if (double.NegativeInfinity.Equals(start))
            {
                start = 0;
            }
            if (double.PositiveInfinity.Equals(start))
            {
                return string.Empty;
            }
            
            var end = TypeConverter.ToNumber(arguments.At(1));
            if (double.PositiveInfinity.Equals(end))
            {
                end = s.Length;
            }

            var len = s.Length;
            var intStart = (int)TypeConverter.ToInteger(start);
            var intEnd = arguments.At(1) == Undefined.Instance ? len : (int)TypeConverter.ToInteger(end);
            var from = intStart < 0 ? System.Math.Max(len + intStart, 0) : System.Math.Min(intStart, len);
            var to = intEnd < 0 ? System.Math.Max(len + intEnd, 0) : System.Math.Min(intEnd, len);
            var span = System.Math.Max(to - from, 0);

            return s.Substring(from, span);
        }

        private JsValue Search(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);

            var s = TypeConverter.ToString(thisObj);

            var regex = arguments.At(0);

            if (regex.IsUndefined())
            {
                regex = string.Empty;
            }
            else if (regex.IsNull())
            {
                regex = Null.Text;
            }

            var rx = TypeConverter.ToObject(Engine, regex) as RegExpInstance ?? (RegExpInstance)Engine.RegExp.Construct(new[] { regex });
            var match = rx.Value.Match(s);
            if (!match.Success)
            {
                return -1;
            }

            return match.Index;
        }

        private JsValue Replace(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);

            var thisString = TypeConverter.ToString(thisObj);
            var searchValue = arguments.At(0);
            var replaceValue = arguments.At(1);

            // If the second parameter is not a function we create one
            var replaceFunction = replaceValue.TryCast<FunctionInstance>();
            if (replaceFunction == null)
            {
                replaceFunction = new ClrFunctionInstance(Engine, (self, args) =>
                {
                    var replaceString = TypeConverter.ToString(replaceValue);
                    var matchValue = TypeConverter.ToString(args.At(0));
                    var matchIndex = (int)TypeConverter.ToInteger(args.At(args.Length - 2));

                    // Check if the replacement string contains any patterns.
                    bool replaceTextContainsPattern = replaceString.IndexOf('$') >= 0;

                    // If there is no pattern, replace the pattern as is.
                    if (replaceTextContainsPattern == false)
                        return replaceString;

                    // Patterns
                    // $$	Inserts a "$".
                    // $&	Inserts the matched substring.
                    // $`	Inserts the portion of the string that precedes the matched substring.
                    // $'	Inserts the portion of the string that follows the matched substring.
                    // $n or $nn	Where n or nn are decimal digits, inserts the nth parenthesized submatch string, provided the first argument was a RegExp object.
                    var replacementBuilder = new StringBuilder();
                    for (int i = 0; i < replaceString.Length; i++)
                    {
                HdrExecutionGuard.Step();
                        char c = replaceString[i];
                        if (c == '$' && i < replaceString.Length - 1)
                        {
                            c = replaceString[++i];
                            if (c == '$')
                                HdrAppend(replacementBuilder,'$');
                            else if (c == '&')
                                HdrAppend(replacementBuilder,matchValue);
                            else if (c == '`')
                                HdrAppend(replacementBuilder,thisString.Substring(0, matchIndex));
                            else if (c == '\'')
                                HdrAppend(replacementBuilder,thisString.Substring(matchIndex + matchValue.Length));
                            else if (c >= '0' && c <= '9')
                            {
                                int matchNumber1 = c - '0';

                                // The match number can be one or two digits long.
                                int matchNumber2 = 0;
                                if (i < replaceString.Length - 1 && replaceString[i + 1] >= '0' && replaceString[i + 1] <= '9')
                                    matchNumber2 = matchNumber1 * 10 + (replaceString[i + 1] - '0');

                                // Try the two digit capture first.
                                if (matchNumber2 > 0 && matchNumber2 < args.Length - 2)
                                {
                                    // Two digit capture replacement.
                                    HdrAppend(replacementBuilder,TypeConverter.ToString(args[matchNumber2]));
                                    i++;
                                }
                                else if (matchNumber1 > 0 && matchNumber1 < args.Length - 2)
                                {
                                    // Single digit capture replacement.
                                    HdrAppend(replacementBuilder,TypeConverter.ToString(args[matchNumber1]));
                                }
                                else
                                {
                                    // Capture does not exist.
                                    HdrAppend(replacementBuilder,'$');
                                    i--;
                                }
                            }
                            else
                            {
                                // Unknown replacement pattern.
                                HdrAppend(replacementBuilder,'$');
                                HdrAppend(replacementBuilder,c);
                            }
                        }
                        else
                            HdrAppend(replacementBuilder,c);
                    }

                    return replacementBuilder.ToString();
                });
            }

            // searchValue is a regular expression

            if (searchValue.IsNull()) 
            {
                searchValue = new JsValue(Null.Text);
            }
            if (searchValue.IsUndefined())
            {
                searchValue = new JsValue(Undefined.Text);
            }
            
            var rx = TypeConverter.ToObject(Engine, searchValue) as RegExpInstance;
            if (rx != null)
            {
                // Replace the input string with replaceText, recording the last match found.
                string result = rx.Value.Replace(thisString, match =>
                {
                    var args = new List<JsValue>();
                    
                    for (var k = 0; k < match.Groups.Count; k++)
                    {
                HdrExecutionGuard.Step();
                        var group = match.Groups[k];
                        args.Add(group.Value);
                    }
                    
                    args.Add(match.Index);
                    args.Add(thisString);

                    var v = TypeConverter.ToString(replaceFunction.Call(Undefined.Instance, args.ToArray()));
                    return v;
                }, rx.Global == true ? -1 : 1);

                // Set the deprecated RegExp properties if at least one match was found.
                //if (lastMatch != null)
                //    this.Engine.RegExp.SetDeprecatedProperties(input, lastMatch);

                return result;
            }

            // searchValue is a string
            else
            {
                var substr = TypeConverter.ToString(searchValue);

                // Find the first occurrance of substr.
                int start = thisString.IndexOf(substr, StringComparison.Ordinal);
                if (start == -1)
                    return thisString;
                int end = start + substr.Length;

                var args = new List<JsValue>();
                args.Add(substr);
                args.Add(start);
                args.Add(thisString);

                var replaceString = TypeConverter.ToString(replaceFunction.Call(Undefined.Instance, args.ToArray()));

                // Replace only the first match.
                var result = new StringBuilder(thisString.Length + (substr.Length - substr.Length));
                HdrAppend(result,thisString, 0, start);
                HdrAppend(result,replaceString);
                HdrAppend(result,thisString, end, thisString.Length - end);
                return result.ToString();
            }
        }

        private JsValue Match(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);

            var s = TypeConverter.ToString(thisObj);

            var regex = arguments.At(0);
            var rx = regex.TryCast<RegExpInstance>();

            rx = rx ?? (RegExpInstance) Engine.RegExp.Construct(new[] {regex});

            var global = rx.Get("global").AsBoolean();
            if (!global)
            {
                return Engine.RegExp.PrototypeObject.Exec(rx, Arguments.From(s));
            }
            else
            {
                rx.Put("lastIndex", 0, false);
                var a = Engine.Array.Construct(Arguments.Empty);
                double previousLastIndex = 0;
                var n = 0;
                var lastMatch = true;
                while (lastMatch)
                {
                HdrExecutionGuard.Step();
                    var result = Engine.RegExp.PrototypeObject.Exec(rx, Arguments.From(s)).TryCast<ObjectInstance>();
                    if (result == null)
                    {
                        lastMatch = false;
                    }
                    else
                    {
                        var thisIndex = rx.Get("lastIndex").AsNumber();
                        if (thisIndex == previousLastIndex)
                        {
                            rx.Put("lastIndex", thisIndex + 1, false);
                            previousLastIndex = thisIndex;
                        }

                        var matchStr = result.Get("0");
                        a.DefineOwnProperty(TypeConverter.ToString(n), new PropertyDescriptor(matchStr, true, true, true), false);
                        n++;
                    }
                }
                if (n == 0)
                {
                    return Null.Instance;
                }
                return a;
            }

        }

        private JsValue LocaleCompare(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);

            var s = TypeConverter.ToString(thisObj);
            var that = TypeConverter.ToString(arguments.At(0));
            
            return string.CompareOrdinal(s, that);
        }

        private JsValue LastIndexOf(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);

            var s = TypeConverter.ToString(thisObj);
            var searchStr = TypeConverter.ToString(arguments.At(0));
            double numPos = double.NaN;
            if (arguments.Length > 1 && arguments[1] != Undefined.Instance)
            {
                numPos = TypeConverter.ToNumber(arguments[1]);
            }

            var pos = double.IsNaN(numPos) ? double.PositiveInfinity : TypeConverter.ToInteger(numPos);

            var len = s.Length;
            var start = (int)System.Math.Min(System.Math.Max(pos, 0), len);
            var searchLen = searchStr.Length;

            var i = start;
            bool found;

            do
            {
                found = true;
                var j = 0;

                while (found && j < searchLen)
                {
                HdrExecutionGuard.Step();
                    if ((i + searchLen > len) || (s[i + j] != searchStr[j]))
                    {
                        found = false;
                    }
                    else
                    {
                        j++;
                    }
                }
                if (!found)
                {
                    i--;
                }

            } while (!found && i >= 0);

            return i;
        }

        private JsValue IndexOf(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);

            var s = TypeConverter.ToString(thisObj);
            var searchStr = TypeConverter.ToString(arguments.At(0));
            double pos = 0;
            if (arguments.Length > 1 && arguments[1] != Undefined.Instance)
            {
                pos = TypeConverter.ToInteger(arguments[1]);
            }

            if (pos >= s.Length)
            {
                return -1;
            }

            if (pos < 0)
            {
                pos = 0;
            }

            return s.IndexOf(searchStr, (int) pos, StringComparison.Ordinal);
        }

        private static System.Text.StringBuilder HdrAppend(System.Text.StringBuilder builder, string value)
        {
            HdrExecutionGuard.Step();
            HdrExecutionGuard.CheckString((long)builder.Length + (value == null ? 0 : value.Length));
            return builder.Append(value);
        }
        private static System.Text.StringBuilder HdrAppend(System.Text.StringBuilder builder, char value)
        {
            HdrExecutionGuard.Step(); HdrExecutionGuard.CheckString((long)builder.Length + 1);
            return builder.Append(value);
        }
        private static System.Text.StringBuilder HdrAppend(System.Text.StringBuilder builder, string value, int start, int count)
        {
            HdrExecutionGuard.Step(); HdrExecutionGuard.CheckString((long)builder.Length + count);
            return builder.Append(value,start,count);
        }

        private JsValue Concat(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);

            var s = TypeConverter.ToString(thisObj);
            var sb = new StringBuilder(s);
            for (int i = 0; i < arguments.Length; i++)
            {
                HdrExecutionGuard.Step();
                var next = TypeConverter.ToString(arguments[i]);
                HdrExecutionGuard.CheckString((long)sb.Length + next.Length);
                Engine.HdrStep();
                HdrAppend(sb,next);
            }

            return sb.ToString();
        }

        private JsValue CharCodeAt(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);

            JsValue pos = arguments.Length > 0 ? arguments[0] : 0;
            var s = TypeConverter.ToString(thisObj);
            var position = (int)TypeConverter.ToInteger(pos);
            if (position < 0 || position >= s.Length)
            {
                return double.NaN;
            }
            return s[position];
        }

        private JsValue CharAt(JsValue thisObj, JsValue[] arguments)
        {
            TypeConverter.CheckObjectCoercible(Engine, thisObj);
            var s = TypeConverter.ToString(thisObj);
            var position = TypeConverter.ToInteger(arguments.At(0));
            var size = s.Length;
            if (position >= size || position < 0)
            {
                return "";
            }
            return s[(int) position].ToString();

        }

        private JsValue ValueOf(JsValue thisObj, JsValue[] arguments)
        {
            var s = thisObj.TryCast<StringInstance>();
            if (s == null)
            {
                throw new JavaScriptException(Engine.TypeError);
            }

            return s.PrimitiveValue;
        }
    }
}
