import QtQuick
import "Protocol.js" as Protocol
import "." as CommandDeck

// Real-process reconnect specs. `false` stands in for a bridge whose daemon is
// missing: it starts and exits at once, like a failed connection. The scenarios
// run concurrently with the rest of the harness, which waits for them to finish.
QtObject {
  id: spec
  required property var host
  property int runningScenarios: 0
  property var waits: []

  property Timer poll: Timer {
    interval: 20
    repeat: true
    onTriggered: spec.checkWaits()
  }

  property CommandDeck.ProtocolClient defaultFixture: CommandDeck.ProtocolClient {
    id: defaultClient
    property int exits: 0
    property var delays: []
    executable: "false"
    enabled: false
    onDisconnected: {
      defaultClient.exits++
      defaultClient.delays = defaultClient.delays.concat([defaultClient.reconnectDelay])
    }
  }

  property CommandDeck.ProtocolClient fastFixture: CommandDeck.ProtocolClient {
    id: fastClient
    property int exits: 0
    property int reconnects: 0
    property var delays: []
    executable: "false"
    enabled: false
    onDisconnected: {
      fastClient.exits++
      fastClient.delays = fastClient.delays.concat([fastClient.reconnectDelay])
    }
    onReconnected: fastClient.reconnects++
  }

  property CommandDeck.CommandDeck deckFixture: CommandDeck.CommandDeck {
    id: reconnectDeck
    visible: false
    testMode: true
  }

  function start() {
    host.pendingAsyncSpecs++
    poll.start()
    scenario("default backoff", verifyDefaultBackoff)
    scenario("backoff reset", verifyReset)
    scenario("open retries at once", verifyOpenRetriesAtOnce)
  }

  // A scenario that throws still reports and releases the harness.
  function scenario(name, body) {
    runningScenarios++
    try {
      body()
    } catch (error) {
      host.expect(false, name + " raised " + error)
      finish()
    }
  }

  function finish() {
    runningScenarios--
    if (runningScenarios > 0) return

    poll.stop()
    host.pendingAsyncSpecs--
  }

  function waitFor(condition, timeout, done) {
    waits = waits.concat([{ condition: condition, deadline: Date.now() + timeout, done: done }])
  }

  function checkWaits() {
    const now = Date.now()
    const ready = waits.filter(value => value.condition() || now >= value.deadline)
    waits = waits.filter(value => ready.indexOf(value) < 0)
    for (const value of ready) value.done(value.condition())
  }

  function verifyDefaultBackoff() {
    defaultClient.enabled = true
    waitFor(() => false, 2500, () => {
      const spawns = defaultClient.exits
      defaultClient.enabled = false
      host.expect(spawns >= 2 && spawns <= 4,
        "a bridge that keeps failing backs off instead of respawning every 500 ms ("
          + spawns + " spawns in 2.5 s)")
      host.expect(defaultClient.delays[0] === 500 && defaultClient.delays[1] === 1000
          && defaultClient.delays.every(delay => delay <= 30000),
        "reconnect waits double from 0.5 s toward the 30 s cap")
      finish()
    })
  }

  function verifyReset() {
    fastClient.reconnectInitialDelay = 40
    fastClient.reconnectMaximumDelay = 160
    fastClient.enabled = true
    waitFor(() => fastClient.exits >= 5, 4000, reached => {
      host.expect(reached && fastClient.delays.slice(0, 4).join(",") === "40,80,160,160",
        "reconnect waits double and stop at the configured cap")
      const exitsBeforeReset = fastClient.exits
      fastClient.handleBridgeMessage({
        requestId: fastClient.reconnectProbeRequestId,
        type: Protocol.Messages.Pong
      })
      host.expect(fastClient.reconnects === 1 && !fastClient.awaitingReconnect
          && fastClient.reconnectDelay === 0,
        "a verified reconnection resets the backoff")
      waitFor(() => fastClient.exits > exitsBeforeReset, 4000, failedAgain => {
        fastClient.enabled = false
        host.expect(failedAgain && fastClient.delays[fastClient.delays.length - 1] === 40,
          "the first failure after a reconnection waits the initial delay again")
        finish()
      })
    })
  }

  function verifyOpenRetriesAtOnce() {
    const client = reconnectDeck.part_protocol
    let exits = 0
    client.disconnected.connect(() => exits++)
    client.executable = "false"
    client.reconnectInitialDelay = 60000
    client.enabled = true
    waitFor(() => exits === 1, 4000, failed => {
      host.expect(failed && !reconnectDeck.daemonAvailable && client.reconnectTimer.running,
        "a failed bridge waits for its backoff while the launcher is closed")
      reconnectDeck.open("{}")
      // The old fixed retry fired 500 ms after a failure; this wait ends earlier.
      waitFor(() => exits === 2, 400, retried => {
        client.enabled = false
        reconnectDeck.close()
        host.expect(retried, "opening the launcher retries a failed bridge at once")
        finish()
      })
    })
  }
}
