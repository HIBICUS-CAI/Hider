using System.Runtime.Versioning;
using Hider.Core;
using Hider.Platform.Linux;
using Tmds.DBus.Protocol;

namespace Hider.App.Tests.Platform
{
    // PortalClient against a private dbus-daemon and the scriptable portal (FakeDesktopPortal).
    [SupportedOSPlatform("linux")]
    public sealed class PortalClientTests
    {
        private const string Screenshot = "org.freedesktop.portal.Screenshot";

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        /// <summary>A Screenshot-shaped request: a string, then options with handle_token and one more string entry.</summary>
        private static Task<PortalClient.Response> Request(PortalClient _client, DBusConnection _bus, CancellationToken? _ct = null, List<string>? _tokens = null)
        {
            return _client.RequestAsync(_bus, Screenshot, "Screenshot", "sa{sv}", (ref MessageWriter _w, string _token) =>
            {
                _tokens?.Add(_token);
                _w.WriteString("parent");
                var d = _w.WriteDictionaryStart();
                PortalClient.Entry(ref _w, "handle_token", _token);
                PortalClient.Entry(ref _w, "extra", "value");
                _w.WriteDictionaryEnd(d);
            }, _ct ?? Ct);
        }

        [Fact]
        public void Constants_AreThePortalsNamesAndTwoMinutes()
        {
            Assert.Equal("org.freedesktop.portal.Desktop", PortalClient.Destination);
            Assert.Equal("/org/freedesktop/portal/desktop", PortalClient.ObjectPath);
            Assert.Equal(TimeSpan.FromSeconds(120), PortalClient.ResponseTimeout);
            using var client = new PortalClient("unix:path=/nonexistent/hider-test-bus");
            Assert.Equal(PortalClient.ResponseTimeout, client.ResponseWait);
        }

        [Fact]
        public void Response_HoldsCodeAndResults()
        {
            var results = new Dictionary<string, VariantValue> { ["uri"] = VariantValue.String("file:///x.png") };
            var response = new PortalClient.Response(2, results);
            Assert.Equal(2u, response.Code);
            Assert.Same(results, response.Results);
            Assert.Equal(response, new PortalClient.Response(2, results));
        }

        [DbusFact]
        public async Task ConnectAsync_ConnectsOnceAndReusesTheConnection()
        {
            using var env = await PortalEnv.StartAsync();
            using var client = new PortalClient(env.Address);
            var first = await client.ConnectAsync(Ct);
            Assert.NotNull(first);
            Assert.StartsWith(":", first!.UniqueName, StringComparison.Ordinal);
            Assert.Same(first, await client.ConnectAsync(Ct));
        }

        [DbusFact]
        public async Task ConnectAsync_Concurrent_MakesOneConnection()
        {
            using var env = await PortalEnv.StartAsync();
            using var client = new PortalClient(env.Address);
            var all = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => client.ConnectAsync(Ct), Ct)));
            Assert.All(all, _c => Assert.Same(all[0], _c));
        }

        [DbusFact]
        public async Task ConnectAsync_NobodyListens_ThrowsAndConnectsOnceTheBusIsThere()
        {
            string dir = PrivateBus.NewDirectory();
            using var client = new PortalClient(PrivateBus.AddressIn(dir));
            await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(Ct));
            // The failed connection is not kept: a second attempt fails the same way, and works once a bus listens.
            await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(Ct));
            using var bus = PrivateBus.Start(dir);
            Assert.NotNull(await client.ConnectAsync(Ct));
        }

        [DbusFact]
        public async Task ConnectAsync_Cancelled_Throws()
        {
            using var env = await PortalEnv.StartAsync();
            using var client = new PortalClient(env.Address);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(cts.Token));
            // The lock was not taken: a later call connects.
            Assert.NotNull(await client.ConnectAsync(Ct));
        }

        [DbusFact]
        public async Task Reset_DropsTheConnection_TheNextCallConnectsAgain()
        {
            using var env = await PortalEnv.StartAsync();
            using var client = new PortalClient(env.Address);
            client.Reset(); // nothing to drop yet
            var first = (await client.ConnectAsync(Ct))!;
            string firstName = first.UniqueName!;
            client.Reset();
            // The dropped connection is disposed.
            Assert.Throws<ObjectDisposedException>(() => first.UniqueName);
            var second = (await client.ConnectAsync(Ct))!;
            Assert.NotSame(first, second);
            Assert.NotEqual(firstName, second.UniqueName);
            var r = await Request(client, second);
            Assert.Equal(0u, r.Code);
        }

        [DbusFact]
        public async Task Dispose_DisposesTheConnection_AndTheClientCannotConnectAgain()
        {
            using var env = await PortalEnv.StartAsync();
            var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            client.Dispose();
            Assert.Throws<ObjectDisposedException>(() => bus.UniqueName);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync(Ct));
        }

        [DbusFact]
        public async Task RequestAsync_Ok_ReturnsTheResults_AndSendsTheRequestTheWayThePortalExpects()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => FakeDesktopPortal.Answer.Ok((ref MessageWriter _w) =>
            {
                FakeDesktopPortal.Entry(ref _w, "uri", "file:///tmp/shot.png");
                FakeDesktopPortal.Entry(ref _w, "n", 5u);
            });
            using var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            var tokens = new List<string>();
            var response = await Request(client, bus, null, tokens);

            Assert.Equal(0u, response.Code);
            Assert.Equal(2, response.Results.Count);
            Assert.Equal("file:///tmp/shot.png", response.Results["uri"].GetString());
            Assert.Equal(5u, response.Results["n"].GetUInt32());

            var call = Assert.Single(env.Portal.Calls);
            Assert.Equal((Screenshot, "Screenshot", PortalClient.ObjectPath), (call.Interface, call.Member, call.Target));
            Assert.Equal("parent", Assert.Single(call.Arguments));
            Assert.Equal(bus.UniqueName, call.Sender);
            // handle_token is what the body writer got: hider<pid>_<n>, and the Request path is built from it.
            string token = Assert.Single(tokens);
            Assert.Matches($"^hider{Environment.ProcessId}_[0-9]+$", token);
            Assert.Equal(token, call.Option("handle_token"));
            Assert.Equal("value", call.Option("extra"));
            string sender = bus.UniqueName!.TrimStart(':').Replace('.', '_');
            Assert.Equal($"/org/freedesktop/portal/desktop/request/{sender}/{token}", call.RequestPath);
            Assert.Empty(env.Portal.Closed);
        }

        [DbusFact]
        public async Task RequestAsync_EachRequestGetsANewToken()
        {
            using var env = await PortalEnv.StartAsync();
            using var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            var tokens = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                await Request(client, bus, null, tokens);
            }
            Assert.Equal(3, tokens.Distinct().Count());
            var numbers = tokens.Select(_t => int.Parse(_t[(_t.LastIndexOf('_') + 1)..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(numbers.Order().ToList(), numbers);
            Assert.Equal(numbers[0] + 2, numbers[2]);
        }

        [DbusFact]
        public async Task RequestAsync_NoResults_IsAnEmptyDictionary()
        {
            using var env = await PortalEnv.StartAsync();
            using var client = new PortalClient(env.Address);
            var response = await Request(client, (await client.ConnectAsync(Ct))!);
            Assert.Equal(0u, response.Code);
            Assert.Empty(response.Results);
        }

        [DbusTheory]
        [InlineData(1u)]
        [InlineData(2u)]
        [InlineData(77u)]
        public async Task RequestAsync_OtherCodes_AreReturnedAsTheyCame(uint _code)
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.Respond(_code, (ref MessageWriter _w) => FakeDesktopPortal.Entry(ref _w, "k", "v"));
            using var client = new PortalClient(env.Address);
            var response = await Request(client, (await client.ConnectAsync(Ct))!);
            Assert.Equal(_code, response.Code);
            Assert.Equal("v", response.Results["k"].GetString());
            Assert.Empty(env.Portal.Closed);
        }

        [DbusFact]
        public async Task RequestAsync_IgnoresTheResponseOfAnotherRequest()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.ElsewhereFirst(0, (ref MessageWriter _w) => FakeDesktopPortal.Entry(ref _w, "mine", "yes"));
            using var client = new PortalClient(env.Address);
            var response = await Request(client, (await client.ConnectAsync(Ct))!);
            Assert.Equal(0u, response.Code);
            Assert.Equal("yes", response.Results["mine"].GetString());
        }

        [DbusFact]
        public async Task RequestAsync_ResponseBeforeTheReply_IsNotMissed()
        {
            // The Response arrives before the method's reply: the match must be in place before the call is sent, or the
            // signal is dropped and the request waits for its timeout (short here, so a regression fails, not stalls).
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.RespondBeforeReply(0, (ref MessageWriter _w) => FakeDesktopPortal.Entry(ref _w, "early", "yes"));
            using var client = new PortalClient(env.Address) { ResponseWait = TimeSpan.FromSeconds(5) };
            var bus = (await client.ConnectAsync(Ct))!;
            var response = await Request(client, bus);
            Assert.Equal(0u, response.Code);
            Assert.Equal("yes", response.Results["early"].GetString());
            Assert.Empty(env.Portal.Closed);
        }

        [DbusFact]
        public async Task RequestAsync_CancelledBeforeTheResponse_ClosesTheRequestAndThrows()
        {
            using var env = await PortalEnv.StartAsync();
            using var cts = new CancellationTokenSource();
            // The portal shows its dialog (no Response) and the caller gives up meanwhile.
            env.Portal.OnRequest = _ =>
            {
                cts.Cancel();
                return new FakeDesktopPortal.Answer.Silent();
            };
            using var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Request(client, bus, cts.Token));
            var call = Assert.Single(env.Portal.Calls);
            Assert.Equal((FakeDesktopPortal.RequestInterface, call.RequestPath!), Assert.Single(env.Portal.Closed));
        }

        [DbusFact]
        public async Task RequestAsync_AlreadyCancelled_StillSendsTheCallAndClosesIt()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.Silent();
            using var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Request(client, bus, cts.Token));
            var call = Assert.Single(env.Portal.Calls);
            Assert.Equal((FakeDesktopPortal.RequestInterface, call.RequestPath!), Assert.Single(env.Portal.Closed));
        }

        [DbusFact]
        public async Task RequestAsync_NoResponseInTime_ClosesTheRequestAndTimesOut()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.Silent();
            using var client = new PortalClient(env.Address) { ResponseWait = TimeSpan.FromMilliseconds(200) };
            var bus = (await client.ConnectAsync(Ct))!;
            await Assert.ThrowsAsync<TimeoutException>(() => Request(client, bus));
            var call = Assert.Single(env.Portal.Calls);
            Assert.Equal((FakeDesktopPortal.RequestInterface, call.RequestPath!), Assert.Single(env.Portal.Closed));
            // The connection is still usable.
            env.Portal.OnRequest = _ => FakeDesktopPortal.Answer.Ok();
            Assert.Equal(0u, (await Request(client, bus)).Code);
        }

        [DbusFact]
        public async Task RequestAsync_CloseFailsAfterATimeout_StillTimesOut_AndLogs()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.Silent();
            env.Portal.FailClose = true;
            using var client = new PortalClient(env.Address) { ResponseWait = TimeSpan.FromMilliseconds(100) };
            var bus = (await client.ConnectAsync(Ct))!;
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                await Assert.ThrowsAsync<TimeoutException>(() => Request(client, bus));
            }
            Assert.Single(env.Portal.Closed);
            Assert.Contains("portal: closing /org/freedesktop/portal/desktop/request/", log.ToString(), StringComparison.Ordinal);
            Assert.Contains("failed: ", log.ToString(), StringComparison.Ordinal);
            Assert.Contains("close failed (test)", log.ToString(), StringComparison.Ordinal);
        }

        [DbusFact]
        public async Task RequestAsync_NoPortalOnTheBus_ThrowsTheCallsError()
        {
            using var env = await PortalEnv.StartAsync(false);
            using var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            var e = await Assert.ThrowsAsync<DBusErrorReplyException>(() => Request(client, bus));
            Assert.Equal("org.freedesktop.DBus.Error.ServiceUnknown", e.ErrorName);
        }

        [DbusFact]
        public async Task RequestAsync_MethodFails_ThrowsTheError_AndClosesNothing()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.Fail("org.freedesktop.portal.Error.NotAllowed");
            using var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            var e = await Assert.ThrowsAsync<DBusErrorReplyException>(() => Request(client, bus));
            Assert.Equal("org.freedesktop.portal.Error.NotAllowed", e.ErrorName);
            Assert.Empty(env.Portal.Closed);
        }

        [DbusFact]
        public async Task RequestAsync_BodyWriterThrows_NothingIsSent_TheClientStillWorks()
        {
            using var env = await PortalEnv.StartAsync();
            using var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.RequestAsync(bus, Screenshot, "Screenshot", "sa{sv}",
                (ref MessageWriter _w, string _token) => throw new InvalidOperationException("test"), Ct));
            Assert.Empty(env.Portal.Calls);
            Assert.Equal(0u, (await Request(client, bus)).Code);
        }

        [DbusFact]
        public async Task CloseObjectAsync_SendsCloseToThatObject()
        {
            using var env = await PortalEnv.StartAsync();
            using var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            await PortalClient.CloseObjectAsync(bus, "/org/freedesktop/portal/desktop/session/1_2/s1", FakeDesktopPortal.SessionInterface);
            await PortalClient.CloseObjectAsync(bus, "/org/freedesktop/portal/desktop/request/1_2/t1", FakeDesktopPortal.RequestInterface);
            Assert.Equal(new[]
            {
                (FakeDesktopPortal.SessionInterface, "/org/freedesktop/portal/desktop/session/1_2/s1"),
                (FakeDesktopPortal.RequestInterface, "/org/freedesktop/portal/desktop/request/1_2/t1"),
            }, env.Portal.Closed);
        }

        [DbusFact]
        public async Task CloseObjectAsync_Failures_AreOnlyLogged()
        {
            using var env = await PortalEnv.StartAsync();
            env.Portal.FailClose = true;
            using var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                // An error reply, and a connection that is gone.
                await PortalClient.CloseObjectAsync(bus, "/org/freedesktop/portal/desktop/session/x/a", FakeDesktopPortal.SessionInterface);
                client.Reset();
                await PortalClient.CloseObjectAsync(bus, "/org/freedesktop/portal/desktop/session/x/b", FakeDesktopPortal.SessionInterface);
            }
            Assert.Single(env.Portal.Closed);
            string text = log.ToString();
            Assert.Contains("portal: closing /org/freedesktop/portal/desktop/session/x/a failed", text, StringComparison.Ordinal);
            Assert.Contains("portal: closing /org/freedesktop/portal/desktop/session/x/b failed", text, StringComparison.Ordinal);
        }

        [DbusFact]
        public async Task RequestAsync_MalformedResponse_FailsAtOnce_AndTheConnectionStays()
        {
            // A Response whose body is not ua{sv}: the request fails with the reader's error instead of waiting for the
            // timeout (two minutes in the app, with a strip transparent meanwhile). The wait here is short enough that a
            // regression fails this test (TimeoutException) instead of stalling the run; the exception type tells "at once"
            // from "timed out".
            using var env = await PortalEnv.StartAsync();
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.Malformed();
            using var client = new PortalClient(env.Address) { ResponseWait = TimeSpan.FromSeconds(5) };
            var bus = (await client.ConnectAsync(Ct))!;
            var e = await Record.ExceptionAsync(() => Request(client, bus));
            Assert.IsType<DBusReadException>(e);
            Assert.Empty(env.Portal.Closed);
            // The connection was kept: the next request works on it.
            env.Portal.OnRequest = _ => FakeDesktopPortal.Answer.Ok();
            Assert.Equal(0u, (await Request(client, bus)).Code);
        }

        [DbusFact]
        public async Task RequestAsync_BusGoesAway_FailsAtOnce()
        {
            var env = await PortalEnv.StartAsync();
            try
            {
                var replied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.Silent();
                env.Portal.Replied = _ => replied.TrySetResult();
                using var client = new PortalClient(env.Address) { ResponseWait = TimeSpan.FromSeconds(5) };
                var bus = (await client.ConnectAsync(Ct))!;
                var request = Request(client, bus);
                await replied.Task.WaitAsync(Ct);
                env.Bus.Dispose(); // the session bus dies while the dialog is open
                // The connection reports its end (not a timeout after the short wait above).
                var e = await Record.ExceptionAsync(() => request);
                Assert.IsType<DBusConnectionClosedException>(e);
            }
            finally
            {
                env.Portal.Dispose();
            }
        }

        [DbusFact]
        public async Task RequestAsync_ResetWhileWaiting_FailsAtOnce()
        {
            // Dispose at exit (or a Reset after another error) drops the connection a request waits on: it ends at once.
            using var env = await PortalEnv.StartAsync();
            var replied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            env.Portal.OnRequest = _ => new FakeDesktopPortal.Answer.Silent();
            env.Portal.Replied = _ => replied.TrySetResult();
            using var client = new PortalClient(env.Address) { ResponseWait = TimeSpan.FromSeconds(5) };
            var bus = (await client.ConnectAsync(Ct))!;
            var request = Request(client, bus);
            await replied.Task.WaitAsync(Ct);
            client.Reset();
            var e = await Record.ExceptionAsync(() => request);
            Assert.IsType<DBusConnectionClosedException>(e);
        }

        [DbusFact]
        public async Task Entry_WritesAStringVariant()
        {
            using var env = await PortalEnv.StartAsync();
            using var client = new PortalClient(env.Address);
            var bus = (await client.ConnectAsync(Ct))!;
            await client.RequestAsync(bus, Screenshot, "Screenshot", "sa{sv}", (ref MessageWriter _w, string _token) =>
            {
                _w.WriteString("");
                var d = _w.WriteDictionaryStart();
                PortalClient.Entry(ref _w, "handle_token", _token);
                PortalClient.Entry(ref _w, "empty", "");
                PortalClient.Entry(ref _w, "unicode", "画面 ✓");
                _w.WriteDictionaryEnd(d);
            }, Ct);
            var call = Assert.Single(env.Portal.Calls);
            Assert.Equal(VariantValueType.String, call.Options["empty"].Type);
            Assert.Equal("", call.Option("empty"));
            Assert.Equal("画面 ✓", call.Option("unicode"));
            Assert.Equal("", Assert.Single(call.Arguments));
        }
    }
}
