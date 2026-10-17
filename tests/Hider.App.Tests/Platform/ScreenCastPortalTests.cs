using System.Runtime.Versioning;
using Hider.Core;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Platform.Linux;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace Hider.App.Tests.Platform
{
    // ScreenCastPortal against a private dbus-daemon and the scriptable portal: CreateSession → SelectSources → Start →
    // OpenPipeWireRemote, and the session closed whenever it is given up.
    [SupportedOSPlatform("linux")]
    public sealed class ScreenCastPortalTests
    {
        private const string ScreenCast = "org.freedesktop.portal.ScreenCast";
        private static readonly string[] fourSteps = { "CreateSession", "SelectSources", "Start", "OpenPipeWireRemote" };
        private static readonly string[] createOnly = { "CreateSession" };

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        /// <summary>What the portal answers per step (the default: a session on monitor node 42 at 1920,0 1280x720, token "t-new").</summary>
        private sealed class Script
        {
            public Func<FakeDesktopPortal.PortalCall, FakeDesktopPortal.Answer>? CreateSession { get; set; }
            public FakeDesktopPortal.Answer? SelectSources { get; set; }
            public FakeDesktopPortal.Answer? Start { get; set; }

            public FakeDesktopPortal.Answer Answer(FakeDesktopPortal.PortalCall _call)
            {
                return _call.Member switch
                {
                    "CreateSession" => CreateSession?.Invoke(_call) ?? SessionCreated(_call),
                    "SelectSources" => SelectSources ?? FakeDesktopPortal.Answer.Ok(),
                    "Start" => Start ?? Started((1920, 0), (1280, 720), "t-new"),
                    _ => new FakeDesktopPortal.Answer.Fail("org.freedesktop.DBus.Error.UnknownMethod"),
                };
            }
        }

        public static string SessionPath(FakeDesktopPortal.PortalCall _call)
        {
            return $"/org/freedesktop/portal/desktop/session/{_call.Sender.TrimStart(':').Replace('.', '_')}/{_call.Option("session_handle_token")}";
        }

        public static FakeDesktopPortal.Answer SessionCreated(FakeDesktopPortal.PortalCall _call)
        {
            string session = SessionPath(_call);
            return FakeDesktopPortal.Answer.Ok((ref MessageWriter _w) => FakeDesktopPortal.Entry(ref _w, "session_handle", session));
        }

        public static FakeDesktopPortal.Answer Started((int X, int Y)? _position, (int W, int H)? _size, string? _token, uint _node = 42)
        {
            return FakeDesktopPortal.Answer.Ok((ref MessageWriter _w) =>
            {
                FakeDesktopPortal.Streams(ref _w, _node, _position, _size);
                if (_token is not null)
                {
                    FakeDesktopPortal.Entry(ref _w, "restore_token", _token);
                }
            });
        }

        private sealed class ScreenCastEnv(PortalEnv _env, Script _script) : IDisposable
        {
            public PortalEnv Env => _env;
            public Script Script => _script;

            public void Dispose() => _env.Dispose();
        }

        /// <summary>A bus and portal answering as <see cref="Script"/> says; OpenPipeWireRemote hands out /dev/null.</summary>
        private static async Task<ScreenCastEnv> StartEnvAsync()
        {
            var env = await PortalEnv.StartAsync();
            var script = new Script();
            env.Portal.OnRequest = script.Answer;
            env.Portal.OnOpenPipeWireRemote = () => File.OpenHandle("/dev/null");
            return new ScreenCastEnv(env, script);
        }

        private static string FdTarget(SafeFileHandle _fd)
        {
            return new FileInfo($"/proc/self/fd/{(int)_fd.DangerousGetHandle()}").LinkTarget!;
        }

        private static string[] Members(PortalEnv _env) => _env.Portal.Calls.Select(_c => _c.Member).ToArray();

        [Fact]
        public void Session_HoldsWhatThePortalGave()
        {
            using var fd = new SafeFileHandle(-1, false);
            var session = new ScreenCastSession("/s/1", 42, new PhysicalRect(1, 2, 3, 4), "tok", fd);
            Assert.Equal(("/s/1", 42u, new PhysicalRect(1, 2, 3, 4), "tok"), (session.Handle, session.NodeId, session.StreamRect, session.RestoreToken));
            Assert.Same(fd, session.PipeWireFd);
            Assert.Equal(session, session with { });
        }

        [DbusFact]
        public async Task StartAsync_Ok_RunsTheFourSteps_AndReturnsTheSession()
        {
            using var sc = await StartEnvAsync();
            var env = sc.Env;
            using var portal = new ScreenCastPortal(env.Address);
            var session = await portal.StartAsync(null, Ct);
            Assert.NotNull(session);
            using var fd = session!.PipeWireFd;

            Assert.Equal(fourSteps, Members(env));
            var calls = env.Portal.Calls;
            Assert.All(calls, _c => Assert.Equal((ScreenCast, PortalClient.ObjectPath), (_c.Interface, _c.Target)));

            // CreateSession: a handle_token and a session_handle_token hider<pid>s<n>.
            var create = calls[0];
            Assert.Empty(create.Arguments);
            Assert.NotNull(create.Option("handle_token"));
            Assert.Matches($"^hider{Environment.ProcessId}s[0-9]+$", create.Option("session_handle_token")!);
            Assert.Equal(2, create.Options.Count);
            string handle = SessionPath(create);
            Assert.Equal(handle, session.Handle);

            // SelectSources: one monitor, cursor hidden, kept until revoked, no restore token on a first start.
            var select = calls[1];
            Assert.Equal(handle, Assert.Single(select.Arguments));
            Assert.Equal(1u, select.Options["types"].GetUInt32());
            Assert.False(select.Options["multiple"].GetBool());
            Assert.Equal(1u, select.Options["cursor_mode"].GetUInt32());
            Assert.Equal(2u, select.Options["persist_mode"].GetUInt32());
            Assert.False(select.Options.ContainsKey("restore_token"));
            Assert.NotNull(select.Option("handle_token"));

            // Start: the session, no parent window.
            var start = calls[2];
            Assert.Equal(new[] { handle, "" }, start.Arguments);
            Assert.Single(start.Options);

            // OpenPipeWireRemote: the session, no options; its fd is handed out.
            var open = calls[3];
            Assert.Equal(handle, Assert.Single(open.Arguments));
            Assert.Empty(open.Options);

            Assert.Equal(42u, session.NodeId);
            Assert.Equal(new PhysicalRect(1920, 0, 1280, 720), session.StreamRect);
            Assert.Equal("t-new", session.RestoreToken);
            Assert.False(session.PipeWireFd.IsInvalid);
            Assert.Equal("/dev/null", FdTarget(session.PipeWireFd));
            Assert.Empty(env.Portal.Closed);
            // Every request has its own handle_token.
            Assert.Equal(3, calls.Take(3).Select(_c => _c.Option("handle_token")).Distinct().Count());
        }

        [DbusFact]
        public async Task StartAsync_WithARestoreToken_PassesIt_AndNoNewTokenIsNull()
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.Start = Started((0, 0), (800, 600), null, 7);
            using var portal = new ScreenCastPortal(env.Address);
            var session = await portal.StartAsync("t-old", Ct);
            using var fd = session!.PipeWireFd;
            Assert.Equal("t-old", env.Portal.Calls[1].Option("restore_token"));
            Assert.Null(session.RestoreToken);
            Assert.Equal(7u, session.NodeId);
            Assert.Equal(new PhysicalRect(0, 0, 800, 600), session.StreamRect);
        }

        [DbusTheory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task StartAsync_StreamWithoutPositionOrSize_LeavesThemZero(bool _position, bool _size)
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.Start = Started(_position ? (-1280, 200) : null, _size ? (2560, 1440) : null, null);
            using var portal = new ScreenCastPortal(env.Address);
            var session = await portal.StartAsync(null, Ct);
            using var fd = session!.PipeWireFd;
            Assert.Equal(new PhysicalRect(_position ? -1280 : 0, _position ? 200 : 0, _size ? 2560 : 0, _size ? 1440 : 0), session.StreamRect);
        }

        [DbusFact]
        public async Task StartAsync_TwoStreams_TakesTheFirst()
        {
            // SelectSources asks for one monitor ("multiple" false); should a portal still hand out two, the first is used.
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.Start = FakeDesktopPortal.Answer.Ok((ref MessageWriter _w) =>
                FakeDesktopPortal.Streams(ref _w, 51, (100, 200), (1024, 768), (52, (-1920, 0), (1920, 1080))));
            using var portal = new ScreenCastPortal(env.Address);
            var session = await portal.StartAsync(null, Ct);
            using var fd = session!.PipeWireFd;
            Assert.Equal(51u, session.NodeId);
            Assert.Equal(new PhysicalRect(100, 200, 1024, 768), session.StreamRect);
            Assert.Equal(fourSteps, Members(env));
        }

        [DbusFact]
        public async Task StartAsync_EachSessionGetsANewToken()
        {
            using var sc = await StartEnvAsync();
            var env = sc.Env;
            using var portal = new ScreenCastPortal(env.Address);
            var first = await portal.StartAsync(null, Ct);
            var second = await portal.StartAsync(null, Ct);
            using var fd1 = first!.PipeWireFd;
            using var fd2 = second!.PipeWireFd;
            Assert.NotEqual(first.Handle, second.Handle);
            var tokens = env.Portal.Calls.Where(_c => _c.Member == "CreateSession").Select(_c => _c.Option("session_handle_token")!).ToList();
            Assert.Equal(2, tokens.Distinct().Count());
        }

        [DbusFact]
        public async Task StartAsync_CreateSessionCancelled_ThrowsDenied_NothingToClose()
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.CreateSession = _ => new FakeDesktopPortal.Answer.Respond(1);
            using var portal = new ScreenCastPortal(env.Address);
            var e = await Assert.ThrowsAsync<CaptureDeniedException>(() => portal.StartAsync(null, Ct));
            Assert.Contains("CreateSession cancelled by the user", e.Message, StringComparison.Ordinal);
            Assert.Equal(createOnly, Members(env));
            Assert.Empty(env.Portal.Closed);
        }

        [DbusFact]
        public async Task StartAsync_SelectSourcesCancelled_ThrowsDenied_AndClosesTheSession()
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.SelectSources = new FakeDesktopPortal.Answer.Respond(1);
            using var portal = new ScreenCastPortal(env.Address);
            var e = await Assert.ThrowsAsync<CaptureDeniedException>(() => portal.StartAsync(null, Ct));
            Assert.Contains("SelectSources cancelled by the user", e.Message, StringComparison.Ordinal);
            Assert.Equal((FakeDesktopPortal.SessionInterface, SessionPath(env.Portal.Calls[0])), Assert.Single(env.Portal.Closed));
        }

        [DbusFact]
        public async Task StartAsync_StartCancelled_ThrowsDenied_AndClosesTheSession()
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.Start = new FakeDesktopPortal.Answer.Respond(1);
            using var portal = new ScreenCastPortal(env.Address);
            var e = await Assert.ThrowsAsync<CaptureDeniedException>(() => portal.StartAsync(null, Ct));
            Assert.Contains("Start cancelled by the user", e.Message, StringComparison.Ordinal);
            Assert.Equal((FakeDesktopPortal.SessionInterface, SessionPath(env.Portal.Calls[0])), Assert.Single(env.Portal.Closed));
            // A refusal keeps the connection.
            script.Start = null;
            var session = await portal.StartAsync(null, Ct);
            using var fd = session!.PipeWireFd;
            Assert.Equal(env.Portal.Calls[0].Sender, env.Portal.Calls[^1].Sender);
        }

        [DbusFact]
        public async Task StartAsync_StartFails_IsNull_ClosesTheSession_AndReconnectsNextTime()
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.Start = new FakeDesktopPortal.Answer.Respond(2);
            using var portal = new ScreenCastPortal(env.Address);
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                Assert.Null(await portal.StartAsync(null, Ct));
            }
            Assert.Contains("screencast portal: InvalidOperationException: screencast portal: Start response 2", log.ToString(), StringComparison.Ordinal);
            Assert.Equal((FakeDesktopPortal.SessionInterface, SessionPath(env.Portal.Calls[0])), Assert.Single(env.Portal.Closed));
            script.Start = null;
            var session = await portal.StartAsync(null, Ct);
            using var fd = session!.PipeWireFd;
            Assert.NotEqual(env.Portal.Calls[0].Sender, env.Portal.Calls[^1].Sender);
        }

        [DbusFact]
        public async Task StartAsync_CreateSessionFails_IsNull_NothingToClose()
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.CreateSession = _ => new FakeDesktopPortal.Answer.Respond(2);
            using var portal = new ScreenCastPortal(env.Address);
            Assert.Null(await portal.StartAsync(null, Ct));
            Assert.Equal(createOnly, Members(env));
            Assert.Empty(env.Portal.Closed);
        }

        [DbusFact]
        public async Task StartAsync_CreateSessionWithoutAHandle_IsNull()
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.CreateSession = _ => FakeDesktopPortal.Answer.Ok();
            using var portal = new ScreenCastPortal(env.Address);
            Assert.Null(await portal.StartAsync(null, Ct));
            Assert.Equal(createOnly, Members(env));
            Assert.Empty(env.Portal.Closed);
        }

        [DbusFact]
        public async Task StartAsync_StartWithoutStreams_IsNull_AndClosesTheSession()
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.Start = FakeDesktopPortal.Answer.Ok((ref MessageWriter _w) => FakeDesktopPortal.Entry(ref _w, "restore_token", "t"));
            using var portal = new ScreenCastPortal(env.Address);
            Assert.Null(await portal.StartAsync(null, Ct));
            Assert.Single(env.Portal.Closed);
            Assert.DoesNotContain("OpenPipeWireRemote", Members(env));
        }

        [DbusFact]
        public async Task StartAsync_StreamsOfAnotherShape_IsNull_AndClosesTheSession()
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            script.Start = FakeDesktopPortal.Answer.Ok((ref MessageWriter _w) => FakeDesktopPortal.Entry(ref _w, "streams", "not streams"));
            using var portal = new ScreenCastPortal(env.Address);
            Assert.Null(await portal.StartAsync(null, Ct));
            Assert.Single(env.Portal.Closed);
        }

        [DbusFact]
        public async Task StartAsync_OpenPipeWireRemoteFails_IsNull_AndClosesTheSession()
        {
            using var sc = await StartEnvAsync();
            var env = sc.Env;
            env.Portal.OnOpenPipeWireRemote = () => null;
            using var portal = new ScreenCastPortal(env.Address);
            Assert.Null(await portal.StartAsync(null, Ct));
            Assert.Equal(fourSteps, Members(env));
            Assert.Equal((FakeDesktopPortal.SessionInterface, SessionPath(env.Portal.Calls[0])), Assert.Single(env.Portal.Closed));
        }

        [DbusFact]
        public async Task StartAsync_CancelledWhileTheDialogIsOpen_ClosesRequestAndSession_AndThrows()
        {
            using var sc = await StartEnvAsync();
            var (env, script) = (sc.Env, sc.Script);
            using var cts = new CancellationTokenSource();
            script.Start = new FakeDesktopPortal.Answer.Silent();
            env.Portal.Replied = _c =>
            {
                if (_c.Member == "Start")
                {
                    cts.Cancel();
                }
            };
            using var portal = new ScreenCastPortal(env.Address);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => portal.StartAsync(null, cts.Token));
            var calls = env.Portal.Calls;
            Assert.Equal(new[]
            {
                (FakeDesktopPortal.RequestInterface, calls[2].RequestPath!),
                (FakeDesktopPortal.SessionInterface, SessionPath(calls[0])),
            }, env.Portal.Closed);
        }

        [DbusFact]
        public async Task StartAsync_NoPortal_IsNull()
        {
            using var env = await PortalEnv.StartAsync(false);
            using var portal = new ScreenCastPortal(env.Address);
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                Assert.Null(await portal.StartAsync(null, Ct));
            }
            Assert.Contains("screencast portal: DBusErrorReplyException", log.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task StartAsync_NoBus_IsNull()
        {
            using var portal = new ScreenCastPortal("unix:path=/nonexistent/hider-test-bus");
            Assert.Null(await portal.StartAsync("t", Ct));
        }

        [DbusFact]
        public async Task CloseAsync_DisposesTheFd_AndClosesTheSession()
        {
            using var sc = await StartEnvAsync();
            var env = sc.Env;
            using var portal = new ScreenCastPortal(env.Address);
            var session = (await portal.StartAsync(null, Ct))!;
            await portal.CloseAsync(session);
            Assert.True(session.PipeWireFd.IsClosed);
            Assert.Equal((FakeDesktopPortal.SessionInterface, session.Handle), Assert.Single(env.Portal.Closed));
        }

        [DbusFact]
        public async Task CloseAsync_CloseFails_OnlyLogs()
        {
            using var sc = await StartEnvAsync();
            var env = sc.Env;
            env.Portal.FailClose = true;
            using var portal = new ScreenCastPortal(env.Address);
            var session = (await portal.StartAsync(null, Ct))!;
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                await portal.CloseAsync(session);
            }
            Assert.True(session.PipeWireFd.IsClosed);
            Assert.Contains($"portal: closing {session.Handle} failed", log.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task CloseAsync_NoBus_DisposesTheFd_AndLogs()
        {
            using var portal = new ScreenCastPortal("unix:path=/nonexistent/hider-test-bus");
            string file = Path.GetTempFileName();
            try
            {
                var session = new ScreenCastSession("/s", 1, default, null, File.OpenHandle(file));
                using var log = new StringWriter();
                using (Log.CaptureTo(log))
                {
                    await portal.CloseAsync(session);
                }
                Assert.True(session.PipeWireFd.IsClosed);
                Assert.Contains("screencast close: ", log.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(file);
            }
        }

        [DbusFact]
        public async Task Dispose_DropsTheConnection_LaterStartsAreNull()
        {
            using var sc = await StartEnvAsync();
            var env = sc.Env;
            var portal = new ScreenCastPortal(env.Address);
            var session = (await portal.StartAsync(null, Ct))!;
            session.PipeWireFd.Dispose();
            portal.Dispose();
            Assert.Null(await portal.StartAsync(null, Ct));
            Assert.Equal(4, env.Portal.Calls.Count);
        }
    }
}
