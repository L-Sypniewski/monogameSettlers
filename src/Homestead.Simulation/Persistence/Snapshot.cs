using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Homestead.Simulation.Persistence;

/// <summary>
/// Writes an object graph – the whole world of a game – to a stream and reads it back, field by field
/// (private ones too) with object identity, so that a loaded game goes on exactly as the saved one would.
/// Nothing in the simulation needs code of its own for it: new state is saved as soon as it is in a field.
/// </summary>
/// <remarks>
/// <para>Format: a type table (name and, for classes and structs, the fields by name with their types),
/// an object table (type and shape of every object: array length, delegate targets), then the contents.
/// Reading creates every object first and fills them after, so references in any direction work. Fields are
/// matched by name: a field that is gone is skipped, a new one keeps its default.</para>
/// <para>Hash-based collections (<see cref="Dictionary{TKey,TValue}"/>, <see cref="HashSet{T}"/>) are saved
/// with their inner layout (order and free slots), and only their hash codes are computed anew – those of
/// strings and objects differ from process to process – so they enumerate in the same order as before.</para>
/// <para>Delegates are kept only when their target belongs to the graph's own assemblies (event handlers of
/// the client are left out; it subscribes again after loading). Fields marked <see cref="NonSerializedAttribute"/>
/// are left out. Types outside the allowed assemblies fail with the path to them.</para>
/// </remarks>
public static class Snapshot
{
    private const BindingFlags DeclaredFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private const byte TagNull = 0, TagRef = 1, TagBoxed = 2, TagString = 3, TagType = 4;
    private const byte KindObject = 0, KindArray = 1, KindDelegate = 2;

    private static readonly string[] AllowedAssemblies =
        ["Homestead.Simulation", "Homestead.Assets", "System.Private.CoreLib", "System.Collections", "System.Linq", "System.Runtime"];

    private static readonly Dictionary<Type, FieldInfo[]> Layouts = new();

    /// <summary>The fields saved for a class or struct: all instance fields up the hierarchy, base first.</summary>
    private static FieldInfo[] LayoutOf(Type type)
    {
        lock (Layouts)
        {
            if (Layouts.TryGetValue(type, out var known))
                return known;
            var chain = new List<Type>();
            for (var t = type; t is not null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType)
                chain.Insert(0, t);
            var fields = chain.SelectMany(t => t.GetFields(DeclaredFields)).Where(f => !f.IsNotSerialized).ToArray();
            Layouts[type] = fields;
            return fields;
        }
    }

    private static string NameOf(FieldInfo field) => field.DeclaringType!.Name + "." + field.Name;

    private static bool Allowed(Type type) =>
        (AllowedAssemblies.Contains(type.Assembly.GetName().Name) || type == typeof(JsonElement)) && type.GetGenericArguments().All(Allowed)
        && (!type.HasElementType || Allowed(type.GetElementType()!));

    /// <summary>Primitive arrays copied as raw bytes (bool and char by <see cref="Buffer"/>, enums by their underlying type).</summary>
    private static bool IsRawElement(Type type) => type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr) || type.IsEnum;

    private static int ByteLength(Array array)
    {
        var element = array.GetType().GetElementType()!;
        return element.IsEnum ? array.Length * Marshal.SizeOf(Enum.GetUnderlyingType(element)) : Buffer.ByteLength(array);
    }

    private static void CopyArray(Array array, byte[] bytes, bool toBytes)
    {
        var element = array.GetType().GetElementType()!;
        if (!element.IsEnum)
        {
            if (toBytes)
                Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
            else
                Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
            return;
        }
        var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
        try
        {
            if (toBytes)
                Marshal.Copy(handle.AddrOfPinnedObject(), bytes, 0, bytes.Length);
            else
                Marshal.Copy(bytes, 0, handle.AddrOfPinnedObject(), bytes.Length);
        }
        finally
        {
            handle.Free();
        }
    }

    // ---------------------------------------------------------------- writing

    public static void Write(Stream stream, object root)
    {
        var writer = new GraphWriter();
        writer.Run(root);
        writer.CopyTo(stream);
    }

    private sealed class GraphWriter
    {
        private readonly MemoryStream _bodyStream = new();
        private readonly BinaryWriter _body;
        private readonly MemoryStream _tableStream = new();
        private readonly BinaryWriter _table;
        private readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> _objects = new();
        private readonly Dictionary<Type, int> _types = new();
        private readonly List<Type> _typeList = new();
        private readonly Dictionary<object, string> _paths = new(ReferenceEqualityComparer.Instance);
        private string _path = "root";

        public GraphWriter()
        {
            _body = new BinaryWriter(_bodyStream);
            _table = new BinaryWriter(_tableStream);
        }

        public void Run(object root)
        {
            WriteTagged(root);
            // Every object's contents, in the order they were found (writing them finds more).
            for (int i = 0; i < _objects.Count; i++)
            {
                var obj = _objects[i];
                _path = _paths[obj];
                WriteContents(obj);
            }
        }

        public void CopyTo(Stream stream)
        {
            var output = new BinaryWriter(stream);
            output.Write(_typeList.Count);
            foreach (var type in _typeList)
            {
                output.Write(type.AssemblyQualifiedName!);
                bool layout = HasLayout(type);
                output.Write(layout);
                if (!layout)
                    continue;
                var fields = LayoutOf(type);
                output.Write(fields.Length);
                foreach (var field in fields)
                {
                    output.Write(NameOf(field));
                    output.Write(_types[field.FieldType]);
                }
            }
            output.Write(_objects.Count);
            output.Write(_tableStream.ToArray());
            output.Write(_bodyStream.ToArray());
            output.Flush();
        }

        private static bool HasLayout(Type type) =>
            !type.IsArray && !type.IsPrimitive && !type.IsEnum && type != typeof(string) && !typeof(Delegate).IsAssignableFrom(type) && !type.IsInterface
            && Nullable.GetUnderlyingType(type) is null && type != typeof(JsonElement);

        private int TypeId(Type type)
        {
            if (_types.TryGetValue(type, out int id))
                return id;
            if (!Allowed(type))
                throw new NotSupportedException($"Cannot save {type.FullName} (at {_path}).");
            id = _typeList.Count;
            _types[type] = id;
            _typeList.Add(type);
            if (type.IsArray)
                TypeId(type.GetElementType()!);
            else if (Nullable.GetUnderlyingType(type) is { } underlying)
                TypeId(underlying);
            else if (HasLayout(type))
                foreach (var field in LayoutOf(type))
                {
                    if (field.FieldType.IsPointer || field.FieldType == typeof(IntPtr) || field.FieldType == typeof(UIntPtr))
                        throw new NotSupportedException($"Cannot save the pointer {NameOf(field)} (at {_path}).");
                    TypeId(field.FieldType);
                }
            return id;
        }

        /// <summary>A value of a known type: primitives and structs directly, everything else tagged.</summary>
        private void WriteRaw(Type type, object? value)
        {
            if (type.IsEnum)
                type = Enum.GetUnderlyingType(type);
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: _body.Write((bool)value!); return;
                case TypeCode.Byte: _body.Write((byte)value!); return;
                case TypeCode.SByte: _body.Write((sbyte)value!); return;
                case TypeCode.Int16: _body.Write((short)value!); return;
                case TypeCode.UInt16: _body.Write((ushort)value!); return;
                case TypeCode.Int32: _body.Write((int)value!); return;
                case TypeCode.UInt32: _body.Write((uint)value!); return;
                case TypeCode.Int64: _body.Write((long)value!); return;
                case TypeCode.UInt64: _body.Write((ulong)value!); return;
                case TypeCode.Single: _body.Write((float)value!); return;
                case TypeCode.Double: _body.Write((double)value!); return;
                case TypeCode.Decimal: _body.Write((decimal)value!); return;
                case TypeCode.Char: _body.Write((ushort)(char)value!); return;
            }
            if (type == typeof(JsonElement))
            {
                // Raw JSON (configuration overrides): as text, parsed again on loading.
                var json = (JsonElement)value!;
                _body.Write(json.ValueKind != JsonValueKind.Undefined);
                if (json.ValueKind != JsonValueKind.Undefined)
                    _body.Write(json.GetRawText());
                return;
            }
            if (Nullable.GetUnderlyingType(type) is { } inner)
            {
                _body.Write(value is not null);
                if (value is not null)
                    WriteRaw(inner, value);
                return;
            }
            if (type.IsValueType)
            {
                TypeId(type);
                string path = _path;
                foreach (var field in LayoutOf(type))
                {
                    _path = path + "." + field.Name;
                    WriteRaw(field.FieldType, field.GetValue(value));
                }
                _path = path;
                return;
            }
            WriteTagged(value);
        }

        private void WriteTagged(object? value)
        {
            switch (value)
            {
                case null:
                    _body.Write(TagNull);
                    return;
                case string text:
                    _body.Write(TagString);
                    _body.Write(text);
                    return;
                case Type type:
                    _body.Write(TagType);
                    _body.Write(TypeId(type));
                    return;
            }
            var actual = value.GetType();
            if (actual.IsValueType)
            {
                _body.Write(TagBoxed);
                _body.Write(TypeId(actual));
                WriteRaw(actual, value);
                return;
            }
            if (value is Delegate del && KeptCalls(del).Count == 0)
            {
                _body.Write(TagNull);
                return;
            }
            _body.Write(TagRef);
            _body.Write(Id(value));
        }

        /// <summary>The calls of a delegate that can be saved: targets in the allowed assemblies.</summary>
        private static List<Delegate> KeptCalls(Delegate del) => del.GetInvocationList()
            .Where(d => (d.Target is null || Allowed(d.Target.GetType())) && Allowed(d.Method.DeclaringType!))
            .ToList();

        private int Id(object obj)
        {
            if (_ids.TryGetValue(obj, out int id))
                return id;
            id = _objects.Count;
            _ids[obj] = id;
            _objects.Add(obj);
            _paths[obj] = _path;
            var type = obj.GetType();
            // The object table: type and shape.
            _table.Write(TypeId(type));
            switch (obj)
            {
                case Array array:
                    if (array.Rank != 1)
                        throw new NotSupportedException($"Cannot save the multi-dimensional array {type} (at {_path}).");
                    _table.Write(KindArray);
                    _table.Write(array.Length);
                    break;
                case Delegate del:
                    _table.Write(KindDelegate);
                    var calls = KeptCalls(del);
                    _table.Write(calls.Count);
                    foreach (var call in calls)
                    {
                        // The target goes into the contents as an ordinary reference; here the method.
                        _table.Write(TypeId(call.Method.DeclaringType!));
                        _table.Write(call.Method.Name);
                        var parameters = call.Method.GetParameters();
                        _table.Write(parameters.Length);
                        foreach (var parameter in parameters)
                            _table.Write(TypeId(parameter.ParameterType));
                        _table.Write(call.Method.IsStatic);
                    }
                    break;
                default:
                    _table.Write(KindObject);
                    break;
            }
            return id;
        }

        private void WriteContents(object obj)
        {
            switch (obj)
            {
                case Array array:
                    var element = array.GetType().GetElementType()!;
                    if (IsRawElement(element))
                    {
                        var bytes = new byte[ByteLength(array)];
                        CopyArray(array, bytes, toBytes: true);
                        _body.Write(bytes);
                    }
                    else
                    {
                        string path = _path;
                        for (int i = 0; i < array.Length; i++)
                        {
                            _path = $"{path}[{i}]";
                            WriteRaw(element, array.GetValue(i));
                        }
                        _path = path;
                    }
                    return;
                case Delegate del:
                    foreach (var call in KeptCalls(del))
                        WriteTagged(call.Target);
                    return;
            }
            string objectPath = _path;
            foreach (var field in LayoutOf(obj.GetType()))
            {
                _path = objectPath + "." + field.Name;
                WriteRaw(field.FieldType, field.GetValue(obj));
            }
            _path = objectPath;
        }
    }

    // ---------------------------------------------------------------- reading

    public static object Read(Stream stream) => new GraphReader(new BinaryReader(stream)).Run();

    public static T Read<T>(Stream stream) => (T)Read(stream);

    private sealed record SavedType(Type? Type, (string Name, int TypeId)[]? Fields);

    private sealed class GraphReader
    {
        private readonly BinaryReader _input;
        private SavedType[] _types = [];
        private object?[] _objects = [];

        public GraphReader(BinaryReader input)
        {
            _input = input;
        }

        public object Run()
        {
            int typeCount = _input.ReadInt32();
            _types = new SavedType[typeCount];
            for (int i = 0; i < typeCount; i++)
            {
                string name = _input.ReadString();
                var type = Type.GetType(name, throwOnError: false);
                (string, int)[]? fields = null;
                if (_input.ReadBoolean())
                {
                    fields = new (string, int)[_input.ReadInt32()];
                    for (int f = 0; f < fields.Length; f++)
                        fields[f] = (_input.ReadString(), _input.ReadInt32());
                }
                _types[i] = new SavedType(type, fields);
            }

            // Every object, empty; delegates once their targets exist.
            int objectCount = _input.ReadInt32();
            _objects = new object?[objectCount];
            var delegates = new List<(int Id, Type Type, List<MethodInfo> Methods)>();
            for (int id = 0; id < objectCount; id++)
            {
                var type = Resolve(_input.ReadInt32());
                byte kind = _input.ReadByte();
                switch (kind)
                {
                    case KindArray:
                        _objects[id] = Array.CreateInstance(type.GetElementType()!, _input.ReadInt32());
                        break;
                    case KindDelegate:
                        int calls = _input.ReadInt32();
                        var methods = new List<MethodInfo>();
                        for (int c = 0; c < calls; c++)
                        {
                            var declaring = Resolve(_input.ReadInt32());
                            string name = _input.ReadString();
                            var parameters = new Type[_input.ReadInt32()];
                            for (int p = 0; p < parameters.Length; p++)
                                parameters[p] = Resolve(_input.ReadInt32());
                            bool isStatic = _input.ReadBoolean();
                            var flags = (isStatic ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.Public | BindingFlags.NonPublic;
                            methods.Add(declaring.GetMethod(name, flags, parameters)
                                        ?? throw new InvalidDataException($"Method {declaring}.{name} is gone."));
                        }
                        delegates.Add((id, type, methods));
                        break;
                    default:
                        _objects[id] = RuntimeHelpers.GetUninitializedObject(type);
                        break;
                }
            }

            var root = ReadTagged();
            var hashed = new List<object>();
            var delegateTargets = new Dictionary<int, List<object?>>();
            int delegateIndex = 0;
            for (int id = 0; id < objectCount; id++)
            {
                if (delegateIndex < delegates.Count && delegates[delegateIndex].Id == id)
                {
                    var targets = new List<object?>();
                    foreach (var _ in delegates[delegateIndex].Methods)
                        targets.Add(ReadTagged(deferDelegates: true));
                    delegateTargets[id] = targets;
                    delegateIndex++;
                    continue;
                }
                ReadContents(_objects[id]!);
                if (IsHashed(_objects[id]!.GetType()))
                    hashed.Add(_objects[id]!);
            }
            BuildDelegates(delegates, delegateTargets);
            foreach (var collection in hashed)
                Rehash(collection);
            return root!;
        }

        private Type Resolve(int typeId) =>
            _types[typeId].Type ?? throw new InvalidDataException("A type of the saved game no longer exists.");

        private readonly List<(object Owner, FieldInfo? Field, int Index, int DelegateId)> _delegateUses = new();

        /// <summary>Delegates are made last; fields and array slots holding one are filled in then.</summary>
        private void BuildDelegates(List<(int Id, Type Type, List<MethodInfo> Methods)> delegates, Dictionary<int, List<object?>> targets)
        {
            foreach (var (id, type, methods) in delegates)
            {
                Delegate? combined = null;
                for (int i = 0; i < methods.Count; i++)
                {
                    var method = methods[i];
                    var single = method.IsStatic
                        ? Delegate.CreateDelegate(type, method)
                        : Delegate.CreateDelegate(type, targets[id][i], method);
                    combined = Delegate.Combine(combined, single);
                }
                _objects[id] = combined;
            }
            foreach (var (owner, field, index, delegateId) in _delegateUses)
            {
                if (field is not null)
                    field.SetValue(owner, _objects[delegateId]);
                else
                    ((Array)owner).SetValue(_objects[delegateId], index);
            }
        }

        private int _pendingDelegateId = -1;

        private object? ReadTagged(bool deferDelegates = false)
        {
            byte tag = _input.ReadByte();
            switch (tag)
            {
                case TagNull:
                    return null;
                case TagString:
                    return _input.ReadString();
                case TagType:
                    return Resolve(_input.ReadInt32());
                case TagBoxed:
                    var type = Resolve(_input.ReadInt32());
                    return ReadRaw(type);
                case TagRef:
                    int id = _input.ReadInt32();
                    if (_objects[id] is null)
                    {
                        // A delegate not made yet: remembered and set once it is.
                        _pendingDelegateId = id;
                        return null;
                    }
                    return _objects[id];
                default:
                    throw new InvalidDataException($"Unknown tag {tag}.");
            }
        }

        /// <summary>Reads a value saved as <paramref name="type"/>; also used to skip fields that are gone.</summary>
        private object? ReadRaw(Type type)
        {
            var actual = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            object? primitive = Type.GetTypeCode(actual) switch
            {
                TypeCode.Boolean => _input.ReadBoolean(),
                TypeCode.Byte => _input.ReadByte(),
                TypeCode.SByte => _input.ReadSByte(),
                TypeCode.Int16 => _input.ReadInt16(),
                TypeCode.UInt16 => _input.ReadUInt16(),
                TypeCode.Int32 => _input.ReadInt32(),
                TypeCode.UInt32 => _input.ReadUInt32(),
                TypeCode.Int64 => _input.ReadInt64(),
                TypeCode.UInt64 => _input.ReadUInt64(),
                TypeCode.Single => _input.ReadSingle(),
                TypeCode.Double => _input.ReadDouble(),
                TypeCode.Decimal => _input.ReadDecimal(),
                TypeCode.Char => (char)_input.ReadUInt16(),
                _ => null,
            };
            if (primitive is not null)
                return type.IsEnum ? Enum.ToObject(type, primitive) : primitive;
            if (type == typeof(JsonElement))
            {
                if (!_input.ReadBoolean())
                    return default(JsonElement);
                using var document = JsonDocument.Parse(_input.ReadString());
                return document.RootElement.Clone();
            }
            if (Nullable.GetUnderlyingType(type) is { } inner)
                return _input.ReadBoolean() ? ReadRaw(inner) : null;
            if (type.IsValueType)
            {
                var boxed = RuntimeHelpers.GetUninitializedObject(type);
                ReadFields(boxed, type);
                return boxed;
            }
            return ReadTagged();
        }

        /// <summary>The saved fields of a class or struct, by name into the current ones.</summary>
        private void ReadFields(object target, Type type)
        {
            var (saved, fields, added) = FieldsOf(type);
            // Fields newer than the save: collections (anything with a parameterless constructor) start empty
            // instead of null; other values keep their default (0, false, null), not their initializer.
            foreach (var field in added)
                if (field.GetValue(target) is null)
                    field.SetValue(target, Activator.CreateInstance(field.FieldType));
            for (int i = 0; i < saved.Length; i++)
            {
                _pendingDelegateId = -1;
                var value = ReadRaw(saved[i]);
                if (fields[i] is not { } field)
                    continue;
                if (_pendingDelegateId >= 0)
                {
                    if (type.IsValueType)
                        throw new NotSupportedException($"A delegate in the struct {type} cannot be loaded.");
                    _delegateUses.Add((target, field, -1, _pendingDelegateId));
                    _pendingDelegateId = -1;
                    continue;
                }
                if (value is null || field.FieldType.IsInstanceOfType(value))
                    field.SetValue(target, value);
            }
        }

        private Dictionary<Type, int>? _typeIndex;
        private readonly Dictionary<Type, (Type[] Saved, FieldInfo?[] Fields, FieldInfo[] Added)> _fieldMaps = new();

        /// <summary>
        /// Per saved field of a type: its saved type and the current field of that name (null: gone); and the
        /// current fields the save does not know that can be made empty (class with a parameterless constructor).
        /// </summary>
        private (Type[] Saved, FieldInfo?[] Fields, FieldInfo[] Added) FieldsOf(Type type)
        {
            if (_fieldMaps.TryGetValue(type, out var map))
                return map;
            var saved = _types[TypeIndex(type)].Fields!;
            var current = LayoutOf(type).ToDictionary(NameOf);
            var savedNames = saved.Select(f => f.Name).ToHashSet();
            var added = current.Where(c => !savedNames.Contains(c.Key)).Select(c => c.Value)
                .Where(f => f.FieldType is { IsClass: true, IsAbstract: false } && f.FieldType != typeof(string)
                            && f.FieldType.GetConstructor(Type.EmptyTypes) is not null)
                .ToArray();
            map = (saved.Select(f => Resolve(f.TypeId)).ToArray(), saved.Select(f => current.GetValueOrDefault(f.Name)).ToArray(), added);
            _fieldMaps[type] = map;
            return map;
        }

        private int TypeIndex(Type type)
        {
            _typeIndex ??= _types.Select((t, i) => (t.Type, i)).Where(x => x.Type is not null)
                .GroupBy(x => x.Type!).ToDictionary(g => g.Key, g => g.First().i);
            return _typeIndex[type];
        }

        private void ReadContents(object obj)
        {
            if (obj is Array array)
            {
                var element = array.GetType().GetElementType()!;
                if (IsRawElement(element))
                {
                    var bytes = _input.ReadBytes(ByteLength(array));
                    CopyArray(array, bytes, toBytes: false);
                    return;
                }
                for (int i = 0; i < array.Length; i++)
                {
                    _pendingDelegateId = -1;
                    var value = ReadRaw(element);
                    if (_pendingDelegateId >= 0)
                    {
                        _delegateUses.Add((array, null, i, _pendingDelegateId));
                        _pendingDelegateId = -1;
                        continue;
                    }
                    array.SetValue(value, i);
                }
                return;
            }
            ReadFields(obj, obj.GetType());
        }

        private static bool IsHashed(Type type) =>
            type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Dictionary<,>) || type.GetGenericTypeDefinition() == typeof(HashSet<>));

        /// <summary>
        /// Computes the hash codes of a dictionary or hash set anew and rebuilds its buckets; the entries keep
        /// their slots (and so their order), free slots stay free.
        /// </summary>
        private static void Rehash(object collection)
        {
            var type = collection.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var buckets = (int[]?)type.GetField("_buckets", flags)!.GetValue(collection);
            if (buckets is null)
                return;
            var entries = (Array)type.GetField("_entries", flags)!.GetValue(collection)!;
            int count = (int)type.GetField("_count", flags)!.GetValue(collection)!;
            var comparer = type.GetField("_comparer", flags)!.GetValue(collection);
            var entryType = entries.GetType().GetElementType()!;
            var entryFields = entryType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var hashField = entryFields.First(f => f.Name.Equals("hashCode", StringComparison.OrdinalIgnoreCase));
            var nextField = entryFields.First(f => f.Name.Equals("next", StringComparison.OrdinalIgnoreCase));
            var keyField = entryFields.First(f => f.Name is "key" or "Value");
            var keyType = keyField.FieldType;
            var hashOf = comparer is null ? null
                : comparer is IEqualityComparer plain ? (Func<object?, int>)(k => k is null ? 0 : plain.GetHashCode(k))
                : GenericHash(comparer, keyType);
            Array.Clear(buckets);
            for (int i = 0; i < count; i++)
            {
                object entry = entries.GetValue(i)!;
                if ((int)nextField.GetValue(entry)! < -1)
                    continue; // a free slot
                var key = keyField.GetValue(entry);
                uint hash = (uint)(hashOf is null ? key!.GetHashCode() : hashOf(key));
                int bucket = (int)(hash % (uint)buckets.Length);
                hashField.SetValue(entry, hashField.FieldType == typeof(uint) ? hash : (object)(int)hash);
                nextField.SetValue(entry, buckets[bucket] - 1);
                entries.SetValue(entry, i);
                buckets[bucket] = i + 1;
            }
        }

        private static Func<object?, int> GenericHash(object comparer, Type keyType)
        {
            var method = typeof(IEqualityComparer<>).MakeGenericType(keyType).GetMethod("GetHashCode")!;
            return key => (int)method.Invoke(comparer, [key])!;
        }
    }
}
