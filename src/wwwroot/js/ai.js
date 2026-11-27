(function () {
    'use strict';

    // WebLLM 0.2.89 supports native tools on selected Hermes models. The game
    // has no qualified GPU model within its current local resource budget.
    const detail = 'Nenhum modelo de GPU com ferramentas está disponível. Use a IA na CPU.';
    const unavailable = () => false;

    window.game = window.game || {};
    window.game.ai = {
        init(bridge) { return bridge?.invokeMethodAsync('OnAiState', 'unsupported', 0, detail); },
        load: unavailable, resume: unavailable, retry: unavailable, disable: unavailable,
        ask: () => null, garden: () => null,
        debug: () => ({}),
        status: () => ({ state: 'unsupported', progress: 0, detail })
    };
})();
