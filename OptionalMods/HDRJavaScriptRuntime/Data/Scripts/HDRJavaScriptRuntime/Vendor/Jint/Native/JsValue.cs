using System;
using System.Collections.Generic;





using Jint.Native.Array;
using Jint.Native.Boolean;

using Jint.Native.Function;
using Jint.Native.Number;
using Jint.Native.Object;
using Jint.Native.RegExp;
using Jint.Native.String;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace Jint.Native
{
    public class JsValue : IEquatable<JsValue>
    {
        public readonly static JsValue Undefined = new JsValue(Types.Undefined);
        public readonly static JsValue Null = new JsValue(Types.Null);
        public readonly static JsValue False = new JsValue(false);
        public readonly static JsValue True = new JsValue(true);

        public JsValue(bool value)
        {
            _double = value ? 1.0 : 0.0;
            _object = null;
            _type = Types.Boolean;
        }

        public JsValue(double value)
        {
            _object = null;
            _type = Types.Number;

            _double = value;
        }

        public JsValue(string value)
        {
            HdrExecutionGuard.ChargeString(value == null ? 0 : value.Length);
            _double = double.NaN;
            _object = value;
            _type = Types.String;
        }

        public JsValue(ObjectInstance value)
        {
            _double = double.NaN;
            _type = Types.Object;

            _object = value;
        }

        private JsValue(Types type)
        {
            _double = double.NaN;
            _object = null;
            _type = type;
        }

        private readonly double _double;

        private readonly object _object;

        private readonly Types _type;
        public bool IsPrimitive()
        {
            return _type != Types.Object && _type != Types.None;
        }
        public bool IsUndefined()
        {
            return _type == Types.Undefined;
        }
        public bool IsArray()
        {
            return IsObject() && AsObject() is ArrayInstance;
        }
        public bool IsDate()
        { return false; }
        public bool IsRegExp()
        {
            return IsObject() && AsObject() is RegExpInstance;
        }
        public bool IsObject()
        {
            return _type == Types.Object;
        }
        public bool IsString()
        {
            return _type == Types.String;
        }
        public bool IsNumber()
        {
            return _type == Types.Number;
        }
        public bool IsBoolean()
        {
            return _type == Types.Boolean;
        }
        public bool IsNull()
        {
            return _type == Types.Null;
        }
        public ObjectInstance AsObject()
        {
            if (_type != Types.Object)
            {
                throw new ArgumentException("The value is not an object");
            }

            return _object as ObjectInstance;
        }
        public ArrayInstance AsArray()
        {
            if (!IsArray())
            {
                throw new ArgumentException("The value is not an array");
            }

            return _object as ArrayInstance;
        }
        public ObjectInstance AsDate()
        { throw new InvalidOperationException("HDR JavaScript: Date is unavailable."); }
        public RegExpInstance AsRegExp()
        {
            if (!IsRegExp())
            {
                throw new ArgumentException("The value is not a date");
            }

            return _object as RegExpInstance;
        }
        public T TryCast<T>(Action<JsValue> fail = null) where T : class
        {
            if (IsObject())
            {
                var o = AsObject();
                var t = o as T;
                if (t != null)
                {
                    return t;
                }
            }

            if (fail != null)
            {
                fail(this);
            }

            return null;
        }

        public bool Is<T>()
        {
            return IsObject() && AsObject() is T;
        }

        public T As<T>() where T : ObjectInstance
        {
            return _object as T;
        }
        public bool AsBoolean()
        {
            if (_type != Types.Boolean)
            {
                throw new ArgumentException("The value is not a boolean");
            }

            return _double != 0;
        }
        public string AsString()
        {
            if (_type != Types.String)
            {
                throw new ArgumentException("The value is not a string");
            }

            if (_object == null)
            {
                throw new ArgumentException("The value is not defined");
            }

            return _object as string;
        }
        public double AsNumber()
        {
            if (_type != Types.Number)
            {
                throw new ArgumentException("The value is not a number");
            }

            return _double;
        }

        public bool Equals(JsValue other)
        {
            if (other == null)
            {
                return false;
            }

            if(ReferenceEquals(this, other))
            {
                return true;
            }

            if (_type != other._type)
            {
                return false;
            }

            switch (_type)
            {
                case Types.None:
                    return false;
                case Types.Undefined:
                    return true;
                case Types.Null:
                    return true;
                case Types.Boolean:
                case Types.Number:
                    return _double == other._double;
                case Types.String:
                case Types.Object:
                    return _object == other._object;
                default:
                    throw new InvalidOperationException();
            }
        }

        public Types Type
        {
            get { return _type; }
        }

        /// <summary>
        /// Creates a valid <see cref="JsValue"/> instance from any <see cref="Object"/> instance
        /// </summary>
        /// <param name="engine"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static JsValue FromObject(Engine engine, object value)
        {
            if (value == null) return Null;
            if (value is string) return new JsValue((string)value);
            if (value is bool) return new JsValue((bool)value);
            if (value is double) return new JsValue((double)value);
            if (value is int) return new JsValue((int)value);
            throw new InvalidOperationException("HDR JavaScript: only explicit scalar values cross the host boundary.");
        }

        /// <summary>
        /// Converts a <see cref="JsValue"/> to its underlying CLR value.
        /// </summary>
        /// <returns>The underlying CLR value of the <see cref="JsValue"/> instance.</returns>
        public object ToObject()
        {
            if (IsNull() || IsUndefined()) return null;
            if (IsString()) return AsString();
            if (IsBoolean()) return AsBoolean();
            if (IsNumber()) return AsNumber();
            throw new InvalidOperationException("HDR JavaScript: JavaScript objects cannot escape to the CLR host.");
        }

        /// <summary>
        /// Invoke the current value as function.
        /// </summary>
        /// <param name="arguments">The arguments of the function call.</param>
        /// <returns>The value returned by the function call.</returns>
        public JsValue Invoke(params JsValue[] arguments)
        {
            return Invoke(Undefined, arguments);
        }

        /// <summary>
        /// Invoke the current value as function.
        /// </summary>
        /// <param name="thisObj">The this value inside the function call.</param>
        /// <param name="arguments">The arguments of the function call.</param>
        /// <returns>The value returned by the function call.</returns>
        public JsValue Invoke(JsValue thisObj, JsValue[] arguments)
        {
            var callable = TryCast<ICallable>();

            if (callable == null)
            {
                throw new ArgumentException("Can only invoke functions");
            }

            return callable.Call(thisObj, arguments);
        }

        public override string ToString()
        {
            switch (Type)
            {
                case Types.None:
                    return "None";
                case Types.Undefined:
                    return "undefined";
                case Types.Null:
                    return "null";
                case Types.Boolean:
                    return _double != 0 ? bool.TrueString : bool.FalseString;
                case Types.Number:
                    return _double.ToString();
                case Types.String:
                case Types.Object:
                    return _object.ToString();
                default:
                    return string.Empty;
            }
        }

        public static bool operator ==(JsValue a, JsValue b)
        {
            if ((object)a == null)
            {
                if ((object)b == null)
                {
                    return true;
                }

                return false;
            }

            return a.Equals(b);
        }

        public static bool operator !=(JsValue a, JsValue b)
        {
            if ((object)a == null)
            {
                if ((object)b == null)
                {
                    return false;
                }

                return true;
            }

            return !a.Equals(b);
        }

        static public implicit operator JsValue(double value)
        {
            return new JsValue(value);
        }

        static public implicit operator JsValue(bool value)
        {
            return new JsValue(value);
        }

        static public implicit operator JsValue(string value)
        {
            return new JsValue(value);
        }

        static public implicit operator JsValue(ObjectInstance value)
        {
            return new JsValue(value);
        }

        internal class JsValueDebugView
        {
            public string Value;
            public JsValueDebugView(JsValue value)
            {

                switch (value.Type)
                {
                    case Types.None:
                        Value = "None";
                        break;
                    case Types.Undefined:
                        Value = "undefined";
                        break;
                    case Types.Null:
                        Value = "null";
                        break;
                    case Types.Boolean:
                        Value = value.AsBoolean() + " (bool)";
                        break;
                    case Types.String:
                        Value = value.AsString() + " (string)";
                        break;
                    case Types.Number:
                        Value = value.AsNumber() + " (number)";
                        break;
                    case Types.Object:
                        Value = value.AsObject().Class;
                        break;
                    default:
                        Value = "Unknown";
                        break;
                }
            }
        }
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is JsValue && Equals((JsValue)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = 0;
                hashCode = (hashCode * 397) ^ _double.GetHashCode();
                hashCode = (hashCode * 397) ^ (_object != null ? _object.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (int)_type;
                return hashCode;
            }
        }
    }
}