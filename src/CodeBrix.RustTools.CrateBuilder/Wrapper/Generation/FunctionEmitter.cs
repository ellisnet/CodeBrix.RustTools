using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// Turns one <see cref="ExportedFunction"/> of the mapping plan into its
/// <c>LibraryImport</c> declaration and its idiomatic managed member.
/// </summary>
/// <remarks>
/// <para>The C convention it binds to (the generated facade): every export except the
/// destroy and release functions returns an <c>int</c> status (0 = success); the result
/// travels through out-parameters that follow the inputs (written only on success): one per
/// value, <c>byte</c> present + value for an optional, one per tuple item, an <c>nint</c>
/// for a handle, the <c>Buffer</c> struct for an owned vector or string. When the plan
/// already lists out-parameters (roles <c>Out*</c>) their positions are used. Strings are
/// UTF-8 pointer + byte length (not NUL-terminated), slices pointer + element count.</para>
/// <para>Pass-through mode declares the crate's own C functions 1:1 and forwards to them.</para>
/// </remarks>
internal sealed class FunctionEmitter
{
    private readonly ManagedTypes _types;
    private readonly string _native;
    private readonly string _exception;

    /// <summary>Creates an emitter.</summary>
    public FunctionEmitter(ManagedTypes types, string exceptionClassName)
    {
        _types = types;
        _native = types.NativeClassName;
        _exception = exceptionClassName;
    }

    /// <summary>Emits one member.</summary>
    /// <param name="function">The planned export.</param>
    /// <param name="placement">Where the member goes.</param>
    /// <param name="memberName">The (already unique) managed name.</param>
    /// <param name="ownerTypeName">The declaring managed type's name.</param>
    /// <param name="mirror">For mirror placements: the mirror.</param>
    /// <param name="handle">For handle placements: the handle type.</param>
    public EmittedMember Emit(ExportedFunction function, MemberPlacement placement, string memberName, string ownerTypeName, MirrorStruct mirror, HandleType handle)
    {
        _types.CurrentClass = ownerTypeName;
        if (placement == MemberPlacement.PassThrough || function.Kind == FunctionKind.PassThrough)
        {
            return EmitPassThrough(function, memberName, ownerTypeName);
        }

        var call = Prepare(function, placement, mirror, handle);
        var member = new EmittedMember
        {
            Name = memberName,
            CName = function.CName,
            ParameterList = string.Join(", ", call.ManagedParameters),
            ArgumentList = string.Join(", ", call.ManagedArguments),
            IsParameterless = call.ManagedParameters.Count == 0,
            Summary = XmlDocText.Summary(function.Doc, PlainSummary(function)),
        };
        member.NativeDeclaration = Declaration(function.CName, "int", call.CParameters);

        var returnType = call.ReturnType;
        var conversion = call.ReturnConversion;
        string modifiers;
        switch (placement)
        {
            case MemberPlacement.FreeFunction:
            case MemberPlacement.MirrorStatic:
            case MemberPlacement.HandleFactory:
                modifiers = "public static unsafe ";
                break;
            case MemberPlacement.HandleConstructorHelper:
                modifiers = "private static unsafe ";
                returnType = "nint";
                conversion = call.RawHandleSlot;
                break;
            default:
                modifiers = "public unsafe ";
                break;
        }

        var isToString = memberName == "ToString" && call.ManagedParameters.Count == 0 && returnType == "string"
            && placement is MemberPlacement.HandleMethod or MemberPlacement.MirrorInstance;
        if (isToString)
        {
            modifiers += "override ";
        }

        var lines = new CodeLines(1);
        lines.Doc(XmlDocText.Lines(function.Doc, DefaultSummary(function)));
        if (placement == MemberPlacement.HandleMethod && function.Kind == FunctionKind.ConsumingMethod)
        {
            lines.Doc(new[] { "<remarks>This call CONSUMES the object (the Rust method takes <c>self</c> by value): afterwards only <see cref=\"Dispose\"/> may be called.</remarks>" });
        }

        lines.Doc(Throws(function, placement));
        lines.Line(modifiers + returnType + " " + memberName + "(" + member.ParameterList + ")");
        lines.Open();
        WriteBody(lines, function, placement, call, conversion);
        lines.Close();
        member.Lines = lines.Lines;
        member.ApiSignature = (placement is MemberPlacement.FreeFunction or MemberPlacement.MirrorStatic or MemberPlacement.HandleFactory ? "static " : string.Empty)
            + returnType + " " + ownerTypeName + "." + memberName + "(" + member.ParameterList + ")";
        return member;
    }

    /// <summary>The declaration of a <c>void (nint handle)</c> destroy function.</summary>
    public static List<string> DestroyDeclaration(string cName) => Declaration(cName, "void", new List<string> { "nint handle" });

    /// <summary>A <c>LibraryImport</c> declaration (indented for the native class).</summary>
    public static List<string> Declaration(string cName, string returnType, IReadOnlyList<string> parameters)
    {
        var lines = new CodeLines(1);
        lines.Line("[LibraryImport(LibraryName, EntryPoint = \"" + cName + "\")]");
        lines.Line("internal static partial " + returnType + " " + CSharpNames.Escape(CSharpNames.Sanitize(cName)) + "(" + string.Join(", ", parameters) + ");");
        return lines.Lines;
    }

    private EmittedMember EmitPassThrough(ExportedFunction function, string memberName, string ownerTypeName)
    {
        var parameters = new List<string>();
        var cParameters = new List<string>();
        var arguments = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Role != ParameterRole.Value && parameter.Role != ParameterRole.Handle)
            {
                throw new WrapperGenerationException($"Pass-through export {function.CName}: parameter '{parameter.RustName}' has role {parameter.Role}; only C value parameters are declared 1:1.");
            }

            var name = Unique(names, ParameterName(parameter));
            var type = parameter.Type ?? throw new WrapperGenerationException($"Pass-through export {function.CName}: parameter '{parameter.RustName}' has no type.");
            parameters.Add(_types.Managed(type) + " " + name);
            cParameters.Add(_types.Interop(type) + " " + name);
            arguments.Add(_types.ToInterop(name, type));
        }

        var returnTypeMapping = function.Return?.Type;
        var returnsValue = function.Return != null && function.Return.Shape != ReturnShape.Void && returnTypeMapping != null && returnTypeMapping.Kind != MappingKind.Unit;
        var managedReturn = returnsValue ? _types.Managed(returnTypeMapping) : "void";
        var interopReturn = returnsValue ? _types.Interop(returnTypeMapping) : "void";
        var call = _native + "." + CSharpNames.Escape(CSharpNames.Sanitize(function.CName)) + "(" + string.Join(", ", arguments) + ")";

        var lines = new CodeLines(1);
        lines.Doc(XmlDocText.Lines(function.Doc, DefaultSummary(function)));
        lines.Doc(new[] { "<remarks>Calls the C export <c>" + XmlDocText.Escape(function.CName) + "</c>.</remarks>" });
        lines.Line("public static " + managedReturn + " " + memberName + "(" + string.Join(", ", parameters) + ")");
        lines.Open();
        lines.Line(returnsValue ? "return " + _types.FromInterop(call, returnTypeMapping) + ";" : call + ";");
        lines.Close();
        return new EmittedMember
        {
            Name = memberName,
            CName = function.CName,
            Lines = lines.Lines,
            ParameterList = string.Join(", ", parameters),
            ArgumentList = string.Join(", ", parameters.Select(p => p.Substring(p.LastIndexOf(' ') + 1))),
            IsParameterless = parameters.Count == 0,
            Summary = XmlDocText.Summary(function.Doc, PlainSummary(function)),
            ApiSignature = "static " + managedReturn + " " + ownerTypeName + "." + memberName + "(" + string.Join(", ", parameters) + ")",
            NativeDeclaration = Declaration(function.CName, interopReturn, cParameters),
        };
    }

    private sealed class CallPlan
    {
        public List<string> ManagedParameters { get; } = new List<string>();

        public List<string> ManagedArguments { get; } = new List<string>();

        public List<string> CParameters { get; } = new List<string>();

        public List<string> CArguments { get; } = new List<string>();

        public List<string> Prologue { get; } = new List<string>();

        public List<(string Variable, string Parameter, bool Optional)> Acquisitions { get; } = new List<(string, string, bool)>();

        public List<string> FixedStatements { get; } = new List<string>();

        public List<string> PinnedStatements { get; } = new List<string>();

        public List<(string Array, string Count, string Parameter)> ListAcquisitions { get; } = new List<(string, string, string)>();

        public List<(string Type, string Variable)> OutLocals { get; } = new List<(string, string)>();

        public bool UsesSelfHandle { get; set; }

        public bool UsesMirrorSelf { get; set; }

        public string ReturnType { get; set; } = "void";

        public string ReturnConversion { get; set; }

        public string RawHandleSlot { get; set; }
    }

    private sealed class OutSlot
    {
        public string InteropType { get; set; }

        public string Variable { get; set; }
    }

    private CallPlan Prepare(ExportedFunction function, MemberPlacement placement, MirrorStruct mirror, HandleType handle)
    {
        var call = new CallPlan();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var parameters = function.Parameters ?? new List<ParameterMapping>();
        var arguments = new string[parameters.Count];
        var cDeclarations = new string[parameters.Count];
        var paired = new bool[parameters.Count];
        var outPositions = new List<int>();
        var cNames = new HashSet<string>(StringComparer.Ordinal);
        var selfIndex = placement is MemberPlacement.HandleMethod ? FindSelfHandle(parameters, handle) : -1;
        var mirrorSelfIndex = placement == MemberPlacement.MirrorInstance ? FindMirrorSelf(parameters, mirror) : -1;
        if (placement == MemberPlacement.HandleMethod && selfIndex < 0)
        {
            throw new WrapperGenerationException($"Method {function.RustPath} ({function.CName}) has no handle parameter for the object it is called on.");
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (paired[i])
            {
                continue;
            }

            var cName = Unique(cNames, CSharpNames.Escape(CSharpNames.Sanitize(parameter.CName ?? parameter.RustName ?? "arg" + i)));

            switch (parameter.Role)
            {
                case ParameterRole.Handle when i == selfIndex:
                    arguments[i] = "__self";
                    cDeclarations[i] = "nint " + cName;
                    call.UsesSelfHandle = true;
                    break;
                case ParameterRole.Handle:
                {
                    var name = Unique(names, ParameterName(parameter));
                    var type = _types.HandleName(parameter.Type);
                    call.ManagedParameters.Add(type + " " + name);
                    call.ManagedArguments.Add(name);
                    call.Prologue.Add("ArgumentNullException.ThrowIfNull(" + name + ");");
                    if (selfIndex >= 0 && function.Kind is FunctionKind.MutMethod or FunctionKind.ConsumingMethod && parameter.Type?.Handle == handle)
                    {
                        call.Prologue.Add("if (ReferenceEquals(" + name + ", this))");
                        call.Prologue.Add("{");
                        call.Prologue.Add("    throw new ArgumentException(\"The object cannot be passed to its own method here (Rust forbids the aliasing).\", nameof(" + name.TrimStart('@') + "));");
                        call.Prologue.Add("}");
                    }

                    var variable = "__h" + i;
                    call.Acquisitions.Add((variable, name, false));
                    arguments[i] = variable;
                    cDeclarations[i] = "nint " + cName;
                    break;
                }

                case ParameterRole.Value when i == mirrorSelfIndex:
                    arguments[i] = "__this";
                    cDeclarations[i] = _types.Interop(parameter.Type) + " " + cName;
                    call.UsesMirrorSelf = true;
                    break;
                case ParameterRole.Value:
                {
                    var name = Unique(names, ParameterName(parameter));
                    call.ManagedParameters.Add(_types.Managed(parameter.Type) + " " + name);
                    call.ManagedArguments.Add(name);
                    arguments[i] = _types.ToInterop(name, parameter.Type);
                    cDeclarations[i] = _types.Interop(parameter.Type) + " " + cName;
                    break;
                }

                case ParameterRole.InPresent when i + 2 < parameters.Count && parameters[i + 1].Role == ParameterRole.StringData && parameters[i + 2].Role == ParameterRole.StringLength:
                {
                    // an optional string: null = None
                    paired[i + 1] = true;
                    paired[i + 2] = true;
                    var text = parameters[i + 1];
                    var name = Unique(names, ParameterName(text));
                    call.ManagedParameters.Add("string " + name);
                    call.ManagedArguments.Add(name);
                    var bytes = "__u" + i;
                    var pointer = "__p" + i;
                    call.Prologue.Add("var " + bytes + " = " + name + " != null ? Encoding.UTF8.GetBytes(" + name + ") : Array.Empty<byte>();");
                    call.FixedStatements.Add("fixed (byte* " + pointer + " = " + bytes + ")");
                    arguments[i] = "(byte)(" + name + " != null ? 1 : 0)";
                    arguments[i + 1] = pointer;
                    arguments[i + 2] = "(nuint)" + bytes + ".Length";
                    cDeclarations[i] = "byte " + cName;
                    cDeclarations[i + 1] = "byte* " + Unique(cNames, CSharpNames.Escape(CSharpNames.Sanitize(text.CName ?? text.RustName ?? "arg" + (i + 1))));
                    cDeclarations[i + 2] = "nuint " + Unique(cNames, CSharpNames.Escape(CSharpNames.Sanitize(parameters[i + 2].CName ?? parameters[i + 2].RustName ?? "len" + (i + 2))));
                    break;
                }

                case ParameterRole.InPresent:
                {
                    var valueIndex = i + 1;
                    if (valueIndex >= parameters.Count || parameters[valueIndex].Role is not (ParameterRole.Value or ParameterRole.Handle))
                    {
                        throw new WrapperGenerationException($"Export {function.CName}: the present flag '{parameter.CName}' is not followed by its value parameter.");
                    }

                    paired[valueIndex] = true;
                    var valueParameter = parameters[valueIndex];
                    var valueCName = Unique(cNames, CSharpNames.Escape(CSharpNames.Sanitize(valueParameter.CName ?? valueParameter.RustName ?? "arg" + valueIndex)));
                    var name = Unique(names, ParameterName(valueParameter));
                    cDeclarations[i] = "byte " + cName;
                    if (valueParameter.Role == ParameterRole.Handle)
                    {
                        call.ManagedParameters.Add(_types.HandleName(valueParameter.Type) + " " + name);
                        call.ManagedArguments.Add(name);
                        var variable = "__h" + valueIndex;
                        call.Acquisitions.Add((variable, name, true));
                        arguments[i] = "(byte)(" + name + " != null ? 1 : 0)";
                        arguments[valueIndex] = variable;
                        cDeclarations[valueIndex] = "nint " + valueCName;
                    }
                    else
                    {
                        var managed = _types.Managed(valueParameter.Type);
                        var nullable = ManagedTypes.IsValueType(valueParameter.Type);
                        call.ManagedParameters.Add(managed + (nullable ? "?" : string.Empty) + " " + name);
                        call.ManagedArguments.Add(name);
                        var test = nullable ? name + ".HasValue" : name + " != null";
                        arguments[i] = "(byte)(" + test + " ? 1 : 0)";
                        arguments[valueIndex] = test + " ? " + _types.ToInterop(nullable ? name + ".Value" : name, valueParameter.Type) + " : default";
                        cDeclarations[valueIndex] = _types.Interop(valueParameter.Type) + " " + valueCName;
                    }

                    break;
                }

                case ParameterRole.SliceData when parameter.Type?.Kind == MappingKind.ValueSlice && parameter.Type.Element?.Kind is MappingKind.Handle or MappingKind.Str or MappingKind.String:
                {
                    var lengthIndex = FindPartner(parameters, paired, i, ParameterRole.SliceLength, function);
                    var name = Unique(names, ParameterName(parameter));
                    var element = ManagedTypes.ValueElement(parameter.Type);
                    var pointer = "__p" + i;
                    if (element.Kind == MappingKind.Handle)
                    {
                        // each object is held (AddRef) for the call; the facade clones it
                        var type = _types.HandleName(element);
                        call.ManagedParameters.Add("ReadOnlySpan<" + type + "> " + name);
                        call.ManagedArguments.Add(name);
                        var handles = "__h" + i;
                        call.Prologue.Add("var " + handles + " = new nint[" + name + ".Length];");
                        call.ListAcquisitions.Add((handles, "__n" + i, name));
                        call.FixedStatements.Add("fixed (nint* " + pointer + " = " + handles + ")");
                        cDeclarations[i] = "nint* " + cName;
                    }
                    else
                    {
                        // every string's UTF-8 bytes in one array, and one str_ref per string
                        call.ManagedParameters.Add("ReadOnlySpan<string> " + name);
                        call.ManagedArguments.Add(name);
                        var offsets = "__o" + i + "s";
                        var bytes = "__u" + i;
                        var refs = "__r" + i;
                        var bytesPointer = "__b" + i;
                        call.Prologue.Add("var " + offsets + " = new int[" + name + ".Length + 1];");
                        call.Prologue.Add("for (var __k = 0; __k < " + name + ".Length; __k++)");
                        call.Prologue.Add("{");
                        call.Prologue.Add("    ArgumentNullException.ThrowIfNull(" + name + "[__k], nameof(" + name.TrimStart('@') + "));");
                        call.Prologue.Add("    " + offsets + "[__k + 1] = checked(" + offsets + "[__k] + Encoding.UTF8.GetByteCount(" + name + "[__k]));");
                        call.Prologue.Add("}");
                        call.Prologue.Add("var " + bytes + " = new byte[" + offsets + "[" + name + ".Length]];");
                        call.Prologue.Add("for (var __k = 0; __k < " + name + ".Length; __k++)");
                        call.Prologue.Add("{");
                        call.Prologue.Add("    Encoding.UTF8.GetBytes(" + name + "[__k], " + bytes + ".AsSpan(" + offsets + "[__k]));");
                        call.Prologue.Add("}");
                        call.Prologue.Add("var " + refs + " = new " + _native + ".StrRef[" + name + ".Length];");
                        call.FixedStatements.Add("fixed (byte* " + bytesPointer + " = " + bytes + ")");
                        call.FixedStatements.Add("fixed (" + _native + ".StrRef* " + pointer + " = " + refs + ")");
                        call.PinnedStatements.Add("for (var __k = 0; __k < " + name + ".Length; __k++)");
                        call.PinnedStatements.Add("{");
                        call.PinnedStatements.Add("    " + refs + "[__k] = new " + _native + ".StrRef { Data = " + bytesPointer + " + " + offsets + "[__k], Length = (nuint)(" + offsets + "[__k + 1] - " + offsets + "[__k]) };");
                        call.PinnedStatements.Add("}");
                        cDeclarations[i] = _native + ".StrRef* " + cName;
                        _types.UsesStrRef = true;
                    }

                    arguments[i] = pointer;
                    arguments[lengthIndex] = "(nuint)" + name + ".Length";
                    cDeclarations[lengthIndex] = "nuint " + Unique(cNames, CSharpNames.Escape(CSharpNames.Sanitize(parameters[lengthIndex].CName ?? parameters[lengthIndex].RustName ?? "len" + lengthIndex)));
                    break;
                }

                case ParameterRole.SliceData when parameter.Type?.Kind == MappingKind.ValueSlice:
                {
                    // the elements are copied into an array of their C form first
                    var lengthIndex = FindPartner(parameters, paired, i, ParameterRole.SliceLength, function);
                    var name = Unique(names, ParameterName(parameter));
                    var element = ManagedTypes.ValueElement(parameter.Type);
                    var interop = _types.Interop(element);
                    call.ManagedParameters.Add("ReadOnlySpan<" + _types.Managed(element) + "> " + name);
                    call.ManagedArguments.Add(name);
                    var copy = "__s" + i;
                    var pointer = "__p" + i;
                    call.Prologue.Add("var " + copy + " = new " + interop + "[" + name + ".Length];");
                    call.Prologue.Add("for (var __k = 0; __k < " + name + ".Length; __k++)");
                    call.Prologue.Add("{");
                    call.Prologue.Add("    " + copy + "[__k] = " + _types.ToInterop(name + "[__k]", element) + ";");
                    call.Prologue.Add("}");
                    call.FixedStatements.Add("fixed (" + interop + "* " + pointer + " = " + copy + ")");
                    arguments[i] = pointer;
                    arguments[lengthIndex] = "(nuint)" + name + ".Length";
                    cDeclarations[i] = interop + "* " + cName;
                    cDeclarations[lengthIndex] = "nuint " + Unique(cNames, CSharpNames.Escape(CSharpNames.Sanitize(parameters[lengthIndex].CName ?? parameters[lengthIndex].RustName ?? "len" + lengthIndex)));
                    break;
                }

                case ParameterRole.SliceData:
                {
                    var lengthIndex = FindPartner(parameters, paired, i, ParameterRole.SliceLength, function);
                    var name = Unique(names, ParameterName(parameter));
                    var element = _types.Element(parameter.Type);
                    var span = (ManagedTypes.IsMutableSlice(parameter.Type) ? "Span<" : "ReadOnlySpan<") + element + ">";
                    call.ManagedParameters.Add(span + " " + name);
                    call.ManagedArguments.Add(name);
                    var pointer = "__p" + i;
                    call.FixedStatements.Add("fixed (" + element + "* " + pointer + " = " + name + ")");
                    arguments[i] = pointer;
                    arguments[lengthIndex] = "(nuint)" + name + ".Length";
                    cDeclarations[i] = element + "* " + cName;
                    cDeclarations[lengthIndex] = "nuint " + Unique(cNames, CSharpNames.Escape(CSharpNames.Sanitize(parameters[lengthIndex].CName ?? parameters[lengthIndex].RustName ?? "len" + lengthIndex)));
                    break;
                }

                case ParameterRole.StringData:
                {
                    var lengthIndex = FindPartner(parameters, paired, i, ParameterRole.StringLength, function);
                    var name = Unique(names, ParameterName(parameter));
                    call.ManagedParameters.Add("string " + name);
                    call.ManagedArguments.Add(name);
                    var bytes = "__u" + i;
                    var pointer = "__p" + i;
                    call.Prologue.Add("ArgumentNullException.ThrowIfNull(" + name + ");");
                    call.Prologue.Add("var " + bytes + " = Encoding.UTF8.GetBytes(" + name + ");");
                    call.FixedStatements.Add("fixed (byte* " + pointer + " = " + bytes + ")");
                    arguments[i] = pointer;
                    arguments[lengthIndex] = "(nuint)" + bytes + ".Length";
                    cDeclarations[i] = "byte* " + cName;
                    cDeclarations[lengthIndex] = "nuint " + Unique(cNames, CSharpNames.Escape(CSharpNames.Sanitize(parameters[lengthIndex].CName ?? parameters[lengthIndex].RustName ?? "len" + lengthIndex)));
                    break;
                }

                case ParameterRole.SliceLength:
                case ParameterRole.StringLength:
                    throw new WrapperGenerationException($"Export {function.CName}: the length parameter '{parameter.RustName}' has no data parameter before it.");
                default:
                    outPositions.Add(i);
                    cDeclarations[i] = cName;
                    break;
            }
        }

        var slots = ReturnSlots(function, handle, call);
        if (outPositions.Count > 0 && outPositions.Count != slots.Count)
        {
            throw new WrapperGenerationException($"Export {function.CName}: the plan lists {outPositions.Count} out-parameter(s) but its result ({function.Return?.Shape}) needs {slots.Count}; the wrapper and the facade would disagree.");
        }

        var cParameters = new List<string>();
        var cArguments = new List<string>();
        var slotIndex = 0;
        for (var i = 0; i < parameters.Count; i++)
        {
            if (outPositions.Contains(i))
            {
                var slot = slots[slotIndex++];
                cParameters.Add(slot.InteropType + "* " + cDeclarations[i]);
                cArguments.Add("&" + slot.Variable);
                continue;
            }

            cParameters.Add(cDeclarations[i]);
            cArguments.Add(arguments[i]);
        }

        if (outPositions.Count == 0)
        {
            for (var k = 0; k < slots.Count; k++)
            {
                cParameters.Add(slots[k].InteropType + "* " + Unique(cNames, "out" + k));
                cArguments.Add("&" + slots[k].Variable);
            }
        }

        foreach (var slot in slots)
        {
            call.OutLocals.Add((slot.InteropType, slot.Variable));
        }

        call.CParameters.AddRange(cParameters);
        call.CArguments.AddRange(cArguments);
        return call;
    }

    private List<OutSlot> ReturnSlots(ExportedFunction function, HandleType owner, CallPlan call)
    {
        var slots = new List<OutSlot>();
        var result = function.Return;
        var shape = result?.Shape ?? ReturnShape.Void;
        var type = result?.Type;
        var tupleItems = result?.TupleItems ?? new List<TypeMapping>();

        if (type != null && tupleItems.Count == 0)
        {
            if (type.Kind == MappingKind.Unit && shape is ReturnShape.Value)
            {
                shape = ReturnShape.Void;
            }
            else if (type.Kind == MappingKind.Unit && shape is ReturnShape.ResultValue)
            {
                shape = ReturnShape.ResultVoid;
            }
            else if (type.Kind == MappingKind.Handle && shape == ReturnShape.Value)
            {
                shape = ReturnShape.Handle;
            }
            else if (type.Kind == MappingKind.Handle && shape == ReturnShape.ResultValue)
            {
                shape = ReturnShape.ResultHandle;
            }
            else if (type.Kind is MappingKind.PrimitiveVec or MappingKind.ValueVec && shape is ReturnShape.Value or ReturnShape.ResultValue)
            {
                shape = shape == ReturnShape.Value ? ReturnShape.OwnedBuffer : ReturnShape.ResultBuffer;
            }
            else if (type.Kind is MappingKind.String or MappingKind.Str && shape is ReturnShape.Value or ReturnShape.ResultValue)
            {
                shape = shape == ReturnShape.Value ? ReturnShape.OwnedString : ReturnShape.ResultString;
            }
        }

        string Add(string interopType)
        {
            var variable = "__o" + slots.Count;
            slots.Add(new OutSlot { InteropType = interopType, Variable = variable });
            return variable;
        }

        switch (shape)
        {
            case ReturnShape.Void:
            case ReturnShape.ResultVoid:
                call.ReturnType = "void";
                break;
            case ReturnShape.Tuple:
            case ReturnShape.ResultTuple:
            case ReturnShape.Value when tupleItems.Count > 0:
            case ReturnShape.ResultValue when tupleItems.Count > 0:
            {
                var items = tupleItems.Count > 0 ? tupleItems : throw new WrapperGenerationException($"Export {function.CName}: a tuple result without items.");
                var conversions = new List<string>();
                foreach (var item in items)
                {
                    conversions.Add(Convert(Add(SlotType(item)), item));
                }

                call.ReturnType = "(" + string.Join(", ", items.Select(_types.Managed)) + ")";
                call.ReturnConversion = "(" + string.Join(", ", conversions) + ")";
                break;
            }

            case ReturnShape.Value:
            case ReturnShape.ResultValue:
                call.ReturnType = _types.Managed(Require(type, function));
                call.ReturnConversion = Convert(Add(SlotType(type)), type);
                break;
            case ReturnShape.Handle:
            case ReturnShape.ResultHandle:
            {
                var name = HandleClass(type, owner, function);
                var variable = Add("nint");
                call.ReturnType = name;
                call.ReturnConversion = name + ".FromNative(" + variable + ")";
                call.RawHandleSlot = variable;
                break;
            }

            case ReturnShape.OptionalHandle:
            {
                var name = HandleClass(type, owner, function);
                var variable = Add("nint");
                call.ReturnType = name;
                call.ReturnConversion = variable + " == 0 ? null : " + name + ".FromNative(" + variable + ")";
                call.RawHandleSlot = variable;
                break;
            }

            case ReturnShape.OwnedBuffer:
            case ReturnShape.ResultBuffer:
            {
                if (type?.Kind == MappingKind.ValueVec)
                {
                    var buffer = Add(_native + ".Buffer");
                    call.ReturnType = _types.Managed(type);
                    call.ReturnConversion = _types.FromInterop(buffer, type);
                    break;
                }

                var element = type == null ? throw Require(function) : type.Kind == MappingKind.PrimitiveVec ? _types.Element(type) : _types.Managed(type);
                var variable = Add(_native + ".Buffer");
                call.ReturnType = element + "[]";
                call.ReturnConversion = _native + ".TakeArray<" + element + ">(" + variable + ")";
                _types.UsesTakeArray = true;
                break;
            }

            case ReturnShape.OwnedString:
            case ReturnShape.ResultString:
            {
                var variable = Add(_native + ".Buffer");
                call.ReturnType = "string";
                call.ReturnConversion = _native + ".TakeString(" + variable + ")";
                _types.UsesTakeString = true;
                break;
            }

            case ReturnShape.OptionalValue:
            {
                Require(type, function);
                var present = Add("byte");
                var inner = Convert(Add(SlotType(type)), type);
                var managed = _types.Managed(type);
                if (ManagedTypes.IsValueType(type))
                {
                    call.ReturnType = managed + "?";
                    call.ReturnConversion = present + " != 0 ? (" + managed + "?)" + Parenthesize(inner) + " : null";
                }
                else
                {
                    call.ReturnType = managed;
                    call.ReturnConversion = present + " != 0 ? " + inner + " : null";
                }

                break;
            }

            default:
                throw new WrapperGenerationException($"Export {function.CName}: result shape {shape} is not supported by the wrapper.");
        }

        return slots;
    }

    private string SlotType(TypeMapping type)
        => type.Kind switch
        {
            MappingKind.Handle => "nint",
            MappingKind.PrimitiveVec or MappingKind.ValueVec or MappingKind.String or MappingKind.Str => _native + ".Buffer",
            _ => _types.Interop(type),
        };

    private string Convert(string variable, TypeMapping type) => _types.FromInterop(variable, type);

    private static string Parenthesize(string expression) => expression.Contains(' ') ? "(" + expression + ")" : expression;

    private string HandleClass(TypeMapping type, HandleType owner, ExportedFunction function)
    {
        if (type?.Handle != null)
        {
            return _types.Qualify(type.Handle.ManagedName);
        }

        var fallback = owner ?? function.Owner;
        if (fallback != null)
        {
            return _types.Qualify(fallback.ManagedName);
        }

        return type != null ? _types.HandleName(type) : throw new WrapperGenerationException($"Export {function.CName}: returns an object, but the plan does not say of which type.");
    }

    private void WriteBody(CodeLines lines, ExportedFunction function, MemberPlacement placement, CallPlan call, string conversion)
    {
        foreach (var line in call.Prologue)
        {
            lines.Line(line);
        }

        if (call.UsesMirrorSelf)
        {
            lines.Line("var __this = ToNative();");
        }

        var closers = 0;
        if (call.UsesSelfHandle)
        {
            lines.Line("lock (_sync)");
            lines.Open();
            lines.Line("var __self = AcquireNativeHandle();");
            lines.Line("try");
            lines.Open();
        }

        foreach (var (variable, parameter, optional) in call.Acquisitions)
        {
            lines.Line("var " + variable + " = " + parameter + (optional ? "?.AcquireNativeHandle() ?? 0;" : ".AcquireNativeHandle();"));
            lines.Line("try");
            lines.Open();
        }

        foreach (var (array, count, parameter) in call.ListAcquisitions)
        {
            lines.Line("var " + count + " = 0;");
            lines.Line("try");
            lines.Open();
            lines.Line("for (; " + count + " < " + parameter + ".Length; " + count + "++)");
            lines.Open();
            lines.Line("ArgumentNullException.ThrowIfNull(" + parameter + "[" + count + "], nameof(" + parameter.TrimStart('@') + "));");
            lines.Line(array + "[" + count + "] = " + parameter + "[" + count + "].AcquireNativeHandle();");
            lines.Close();
            lines.Line(string.Empty);
        }

        foreach (var statement in call.FixedStatements)
        {
            lines.Line(statement);
            lines.Open();
            closers++;
        }

        foreach (var statement in call.PinnedStatements)
        {
            lines.Line(statement);
        }

        foreach (var (type, variable) in call.OutLocals)
        {
            lines.Line(type + " " + variable + " = default;");
        }

        lines.Line("var __status = " + _native + "." + CSharpNames.Escape(CSharpNames.Sanitize(function.CName)) + "(" + string.Join(", ", call.CArguments) + ");");
        if (function.Kind == FunctionKind.ConsumingMethod && call.UsesSelfHandle)
        {
            lines.Line("if (__status != (int)" + _native + ".Status.NullArgument && __status != (int)" + _native + ".Status.InvalidArgument)");
            lines.Open();
            lines.Line("_consumed = true;");
            lines.Close();
            lines.Line(string.Empty);
        }

        lines.Line("if (__status != 0)");
        lines.Open();
        lines.Line("throw " + _native + ".Failure(__status);");
        lines.Close();
        if (call.ReturnType != "void" || placement == MemberPlacement.HandleConstructorHelper)
        {
            lines.Line(string.Empty);
            lines.Line("return " + conversion + ";");
        }

        for (var i = 0; i < closers; i++)
        {
            lines.Close();
        }

        for (var i = call.ListAcquisitions.Count - 1; i >= 0; i--)
        {
            var (_, count, parameter) = call.ListAcquisitions[i];
            lines.Close();
            lines.Line("finally");
            lines.Open();
            lines.Line("for (var __k = 0; __k < " + count + "; __k++)");
            lines.Open();
            lines.Line(parameter + "[__k].ReleaseNativeHandle();");
            lines.Close();
            lines.Close();
        }

        for (var i = call.Acquisitions.Count - 1; i >= 0; i--)
        {
            lines.Close();
            lines.Line("finally");
            lines.Open();
            lines.Line(call.Acquisitions[i].Parameter + (call.Acquisitions[i].Optional ? "?" : string.Empty) + ".ReleaseNativeHandle();");
            lines.Close();
        }

        if (call.UsesSelfHandle)
        {
            lines.Close();
            lines.Line("finally");
            lines.Open();
            lines.Line("ReleaseNativeHandle();");
            lines.Close();
            lines.Close();
        }
    }

    private IEnumerable<string> Throws(ExportedFunction function, MemberPlacement placement)
    {
        var shape = function.Return?.Shape ?? ReturnShape.Void;
        var isResult = shape is ReturnShape.ResultValue or ReturnShape.ResultHandle or ReturnShape.ResultVoid or ReturnShape.ResultBuffer or ReturnShape.ResultString or ReturnShape.ResultTuple;
        var error = string.IsNullOrWhiteSpace(function.Return?.ErrorTypeDisplay) ? string.Empty : " (<c>" + XmlDocText.Escape(function.Return.ErrorTypeDisplay) + "</c>)";
        yield return "<exception cref=\"" + _exception + "\">" + (isResult ? "The Rust function returned an error" + error + "; the message is its text." : "The native call failed (an argument the native side rejected, or a panic inside the crate).") + "</exception>";
        if (placement is MemberPlacement.HandleMethod)
        {
            yield return "<exception cref=\"ObjectDisposedException\">The object was disposed.</exception>";
            yield return "<exception cref=\"InvalidOperationException\">The object was consumed by an earlier call.</exception>";
        }
    }

    private static string PlainSummary(ExportedFunction function) => "Calls " + (function.RustPath ?? function.CName) + ".";

    private static string DefaultSummary(ExportedFunction function) => "Calls <c>" + XmlDocText.Escape(function.RustPath ?? function.CName) + "</c>.";

    private static int FindSelfHandle(IList<ParameterMapping> parameters, HandleType handle)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Role == ParameterRole.Handle && (parameters[i].RustName == "self" || parameters[i].Type?.Handle == null || parameters[i].Type.Handle == handle))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindMirrorSelf(IList<ParameterMapping> parameters, MirrorStruct mirror)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter.Role == ParameterRole.Value && parameter.RustName == "self" && parameter.Type?.Kind == MappingKind.MirrorStruct && (parameter.Type.Mirror == null || parameter.Type.Mirror == mirror))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindPartner(IList<ParameterMapping> parameters, bool[] paired, int dataIndex, ParameterRole role, ExportedFunction function)
    {
        for (var j = dataIndex + 1; j < parameters.Count; j++)
        {
            if (parameters[j].Role == role && !paired[j])
            {
                paired[j] = true;
                return j;
            }
        }

        throw new WrapperGenerationException($"Export {function.CName}: the {parameters[dataIndex].Role} parameter '{parameters[dataIndex].RustName}' has no {role} parameter after it.");
    }

    private static string ParameterName(ParameterMapping parameter)
    {
        var raw = !string.IsNullOrWhiteSpace(parameter.ManagedName) ? parameter.ManagedName : parameter.RustName;
        return CSharpNames.Camel(CSharpNames.Sanitize(raw ?? "arg"));
    }

    private static string Unique(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++)
        {
            candidate = name + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    private static TypeMapping Require(TypeMapping type, ExportedFunction function)
        => type ?? throw Require(function);

    private static WrapperGenerationException Require(ExportedFunction function)
        => new WrapperGenerationException($"Export {function.CName}: the plan gives no result type.");
}
