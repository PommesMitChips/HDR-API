using System;
using Hdr.Mods;
using VRage;

namespace HDRJavaScriptRuntime
{
    public sealed partial class JavaScriptRuntimeSession
    {
        object Service(string command, object[] values)
        {
            if (!active) throw new InvalidOperationException("HDR JavaScript client service is stopped.");
            var a = new Arguments(values);
            if (command == "version") { a.End(); return Protocol; }
            if (command == "capabilities")
            {
                a.End(); return new[] { JavaScriptRealm.Profile, JavaScriptDocumentBridge.Profile,
                    "client-local", "plugin-free-interpreter", "explicit-document-binding", "atomic-html-mutations",
                    "scalar-host-values", "named-functions", "document-events", "document-timers",
                    "declared-source-choices", "owned-hud-cooperative-pointer", "no-clr", "no-native-input", "no-network-fetch", "no-module-imports" };
            }
            if (command != "open") throw new ArgumentException("Unknown HDR JavaScript service command: " + command);
            RequireIdle(); string id = a.Text(); a.End(); ValidateOwner(id);
            busy = true;
            try
            {
                Owner prior;
                if (owners.TryGetValue(id, out prior)) ReleaseOwner(prior);
                if (nextGeneration == long.MaxValue) throw new InvalidOperationException("JavaScript owner generation space exhausted.");
                var owner = new Owner { Id = id, Generation = ++nextGeneration };
                owners[id] = owner;
                return new Func<string, object[], object>((op, args) => Command(owner, op, args));
            }
            finally { busy = false; }
        }
        object Command(Owner owner, string command, object[] values)
        {
            var a = new Arguments(values);
            if (command == "valid") { a.End(); return Current(owner); }
            if (!Current(owner)) throw new InvalidOperationException("HDR JavaScript owner endpoint is retired.");
            RequireIdle(); busy = true;
            try
            {
                if (command == "generation") { a.End(); return owner.Generation; }
                if (command == "release") { a.End(); ReleaseOwner(owner); owners.Remove(owner.Id); return true; }
                if (command == "configure")
                {
                    var settings = a.Settings(); a.End(); Configure(owner, settings); return true;
                }
                if (command == "limits") { a.End(); return Settings(owner.Limits, owner.HostLimits, owner.MaxRealms); }
                if (command == "create")
                {
                    a.End(); RequireRoom(owner);
                    return AddRealm(owner, new JavaScriptRealm(owner.Limits)).Handle;
                }
                if (command == "bind-html") return BindHtml(owner, a);
                if (command == "create-hud") return CreateHud(owner, a);
                if (command == "clear-owned")
                {
                    a.End(); foreach (var item in owner.Realms.Values) RetireRealm(item);
                    owner.Realms.Clear(); return true;
                }
                long handle = a.Long(); Realm realm;
                if (!owner.Realms.TryGetValue(handle, out realm)) throw new ArgumentException("Realm does not belong to this JavaScript owner.");
                if (command == "status")
                {
                    a.End(); bool current = realm.Current;
                    return new MyTuple<bool, string, string, MyTuple<long, long, bool>, long>(current,
                        realm.LastError ?? (realm.Bridge == null ? null : realm.Bridge.LastError), JavaScriptRealm.Profile,
                        new MyTuple<long, long, bool>(owner.Generation, realm.HtmlGeneration, realm.Host != null), realm.Document);
                }
                if (command == "diagnostics")
                {
                    bool clear = a.Has ? a.Flag() : true; a.End();
                    var result = realm.Diagnostics.ToArray();
                    if (clear) realm.Diagnostics.Clear(); return result;
                }
                if (command == "dispose") { a.End(); RetireRealm(realm); owner.Realms.Remove(handle); return true; }
                if (command == "pointer-cancel")
                {
                    a.End(); RequireOwnedHud(realm); owner.Html.CancelPointer(realm.Document); return true;
                }
                if (command == "realm-limits") { a.End(); return Settings(realm.Interpreter.Limits, realm.HostLimits, owner.MaxRealms); }
                if (!realm.Current) throw new InvalidOperationException(realm.LastError ?? "JavaScript realm or its owned HTML document is retired.");
                if (command == "pointer")
                {
                    double x = a.Number(), y = a.Number(); bool pressed = a.Flag(); a.End();
                    RequireOwnedHud(realm); owner.Html.Pointer(realm.Document, x, y, pressed); return true;
                }
                if (command == "execute")
                {
                    string source = a.Text(); a.End();
                    try
                    {
                        bool result;
                        if (realm.Bridge == null) { realm.Interpreter.Execute(source); result = true; }
                        else if (!realm.Registered)
                        { realm.Bridge.Update(now); result = realm.Bridge.RegisterScript(source); realm.Registered = true; }
                        else result = realm.Bridge.Execute(source);
                        realm.LastError = result ? null : realm.Bridge.LastError;
                        return result;
                    }
                    catch (Exception error) { realm.LastError = error.Message; throw; }
                }
                if (command == "call")
                {
                    string name = a.Text(); object[] args = a.Scalars(); a.End();
                    if (realm.Bridge != null && !realm.Registered) throw new InvalidOperationException("Execute the document's initial script before invoking handlers.");
                    try
                    {
                        object result = realm.Bridge == null ? realm.Interpreter.InvokeNamed(name, args) : realm.Bridge.InvokeNamed(name, args);
                        realm.LastError = null; return result;
                    }
                    catch (Exception error) { realm.LastError = error.Message; throw; }
                }
                if (command == "source-choice")
                {
                    if (realm.Host == null) throw new ArgumentException("Source choices require an explicitly bound HTML document.");
                    string choice = a.Text(), node = a.Text(), provider = a.Text(), source = a.Text();
                    var probe = a.Probe(); var settings = a.Has ? a.Settings() : null; a.End();
                    realm.Host.AddSourceChoice(choice, node, provider, source, probe, settings); return true;
                }
                throw new ArgumentException("Unknown HDR JavaScript owner command: " + command);
            }
            finally { busy = false; }
        }
        Realm AddRealm(Owner owner, JavaScriptRealm interpreter)
        {
            if (!Current(owner)) { interpreter.Dispose(); throw new InvalidOperationException("JavaScript owner retired during realm admission."); }
            if (nextRealm == long.MaxValue) { interpreter.Dispose(); throw new InvalidOperationException("JavaScript realm handle space exhausted."); }
            var realm = new Realm { Handle = ++nextRealm, Owner = owner, Interpreter = interpreter, HostLimits = owner.HostLimits.Copy() };
            owner.Realms.Add(realm.Handle, realm); return realm;
        }
        void RequireRoom(Owner owner)
        {
            var retired = new System.Collections.Generic.List<long>();
            int live = 0;
            foreach (var item in owner.Realms.Values)
                if (!item.Released) live++;
                else if (!item.OwnsDocument || item.OwnedDocumentReleased) retired.Add(item.Handle);
            foreach (long handle in retired) owner.Realms.Remove(handle);
            if (owner.MaxRealms > 0 && live >= owner.MaxRealms)
                throw new InvalidOperationException("Configured JavaScript realm limit exceeded; dispose an owned realm or configure max-realms.");
        }
        long BindHtml(Owner owner, Arguments a)
        {
            var endpoint = a.Endpoint(); long document = a.Long(), generation = a.Long(); var current = a.Witness();
            string[] nodes = a.Strings(), data = a.Strings(); a.End(); RequireRoom(owner);
            var host = new HdrHtmlDocumentHost(endpoint, document, generation, current, nodes, data);
            if (!Current(owner)) { host.Dispose(); throw new InvalidOperationException("JavaScript owner retired during HTML binding."); }
            return BindHost(owner, host, document, generation, false);
        }
        long BindHost(Owner owner, HdrHtmlDocumentHost host, long document, long generation, bool owned)
        {
            Realm prior;
            if (documentBindings.TryGetValue(document, out prior))
            {
                if (prior.Current) { host.Dispose(); throw new InvalidOperationException("This HTML document already has an active JavaScript realm; dispose it before binding another."); }
                RetireRealm(prior, false);
            }
            JavaScriptRealm interpreter = null; Realm realm = null;
            try
            {
                interpreter = new JavaScriptRealm(owner.Limits);
                realm = AddRealm(owner, interpreter); realm.Host = host; realm.Document = document;
                realm.HtmlGeneration = generation; realm.OwnsDocument = owned;
                realm.Bridge = new JavaScriptDocumentBridge(interpreter, host, realm.HostLimits,
                    value => RecordDiagnostic(realm, value));
                if (!Current(owner) || !host.Current) throw new InvalidOperationException("HTML owner/document retired during JavaScript binding.");
                documentBindings[document] = realm; return realm.Handle;
            }
            catch
            {
                if (realm != null) { RetireRealm(realm); owner.Realms.Remove(realm.Handle); }
                else { host.Dispose(); if (interpreter != null) interpreter.Dispose(); }
                throw;
            }
        }
        long CreateHud(Owner owner, Arguments a)
        {
            string markup = a.Text(), css = a.Text(); double width = a.Number(), height = a.Number();
            string[] nodes = a.Strings(), data = a.Strings(); string backend = a.Has ? a.Text() : "vector"; a.End(); RequireRoom(owner);
            if (owner.Html == null) owner.Html = new HdrHtmlApi("hdrjs." + owner.Id);
            if (!owner.Html.Ready) { owner.Html.Request(); if (!owner.Html.Ready) throw new InvalidOperationException("Requires mod: HDR HTML Frontend (client service unavailable)."); }
            long document = owner.Html.CreateHud(markup, css, width, height, backend);
            try
            {
                var host = new HdrHtmlDocumentHost(owner.Html, document, nodes, data);
                return BindHost(owner, host, document, owner.Html.ConnectionGeneration, true);
            }
            catch { try { owner.Html.Destroy(document); } catch { } throw; }
        }
        static void ValidateOwner(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 40) throw new ArgumentException("JavaScript owner ID requires 1..40 characters.");
            foreach (char c in id) if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
                throw new ArgumentException("JavaScript owner ID requires letters, digits, period, underscore or hyphen.");
        }
        static void RequireOwnedHud(Realm realm)
        {
            if (!realm.OwnsDocument)
                throw new InvalidOperationException("Cooperative input on an externally bound HTML document belongs to its HTML owner; use that owner's pointer API.");
            if (realm.OwnedDocumentReleased || realm.Owner.Html == null || !realm.Owner.Html.Ready ||
                realm.Owner.Html.ConnectionGeneration != realm.HtmlGeneration)
                throw new InvalidOperationException("Runtime-owned HTML document owner/generation is retired.");
            // Status verifies this actual document still belongs to the exact current owner endpoint.
            realm.Owner.Html.Status(realm.Document);
            if (!realm.Owner.Html.Ready || realm.Owner.Html.ConnectionGeneration != realm.HtmlGeneration)
                throw new InvalidOperationException("Runtime-owned HTML endpoint changed while validating cooperative input.");
        }
    }
}
