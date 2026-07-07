let dotNetRef = null;
let timer = null;

export function initialize(ref) {
    dotNetRef = ref;
    console.log("[InteropSpike] module imported + path resolved OK");
    // immediate JS -> .NET roundtrip
    dotNetRef.invokeMethodAsync("PingFromJs", "hello from JS @ " + new Date().toISOString());
    // recurring roundtrip proves the circuit stays live
    timer = setInterval(() => {
        dotNetRef?.invokeMethodAsync("PingFromJs", "tick " + Date.now());
    }, 2000);
}

export function dispose() {
    console.log("[InteropSpike] dispose() called — clearing timer");
    if (timer) clearInterval(timer);
    timer = null;
    dotNetRef = null;
}
