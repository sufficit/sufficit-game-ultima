// Ultima — voz: microfone do usuário (captura + nível, enviada aos peers), fala
// sintetizada da Guardiã e ditado por reconhecimento de fala nos diálogos.
(function () {
    'use strict';

    let dotnet = null;
    let stream = null;
    let audioCtx = null;
    let raf = 0;
    let lastLevelSent = 0;

    const SR = window.SpeechRecognition || window.webkitSpeechRecognition || null;
    let recog = null;
    let listening = false;

    function state(text) {
        if (dotnet) { dotnet.invokeMethodAsync('OnMicState', text); }
    }

    async function startMic() {
        if (stream) { return; }
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            state('erro: captura de áudio indisponível neste navegador');
            return;
        }
        try {
            stream = await navigator.mediaDevices.getUserMedia({
                audio: { echoCancellation: true, noiseSuppression: true }
            });
            audioCtx = new (window.AudioContext || window.webkitAudioContext)();
            const source = audioCtx.createMediaStreamSource(stream);
            const analyser = audioCtx.createAnalyser();
            analyser.fftSize = 512;
            source.connect(analyser);
            const buf = new Uint8Array(analyser.frequencyBinCount);
            const loop = () => {
                analyser.getByteTimeDomainData(buf);
                let sum = 0;
                for (let i = 0; i < buf.length; i++) {
                    const v = (buf[i] - 128) / 128;
                    sum += v * v;
                }
                const level = Math.min(1, Math.sqrt(sum / buf.length) * 3.5);
                const now = performance.now();
                if (now - lastLevelSent > 150 && dotnet) {
                    lastLevelSent = now;
                    dotnet.invokeMethodAsync('OnMicLevel', level);
                }
                raf = requestAnimationFrame(loop);
            };
            loop();
            state('ligada');
            if (window.game && game.net && game.net.attachStream) { game.net.attachStream(stream); }
        } catch (e) {
            stream = null;
            const why = e && e.name === 'NotAllowedError' ? 'permissão negada'
                : e && e.name === 'NotFoundError' ? 'microfone não encontrado'
                : 'falha ao abrir o microfone';
            state('erro: ' + why);
        }
    }

    function stopMic() {
        if (raf) { cancelAnimationFrame(raf); raf = 0; }
        if (stream) { for (const track of stream.getTracks()) { track.stop(); } stream = null; }
        if (audioCtx) { audioCtx.close().catch(() => { /* contexto já fechado */ }); audioCtx = null; }
        if (window.game && game.net && game.net.detachStream) { game.net.detachStream(); }
        if (dotnet) { dotnet.invokeMethodAsync('OnMicLevel', 0); }
        state('desligada');
    }

    function pickVoice() {
        try {
            const voices = speechSynthesis.getVoices();
            return voices.find((v) => v.lang === 'pt-BR' && /luciana|francisca|maria|female|mulher/i.test(v.name))
                || voices.find((v) => v.lang === 'pt-BR')
                || voices.find((v) => v.lang && v.lang.indexOf('pt') === 0)
                || null;
        } catch (e) { return null; }
    }

    function speak(text, speaker) {
        try {
            if (!window.speechSynthesis) { return; }
            const clean = String(text || '').slice(0, 400);
            if (!clean) { return; }
            const utterance = new SpeechSynthesisUtterance(clean);
            utterance.lang = 'pt-BR';
            utterance.rate = 1.03;
            utterance.pitch = speaker && /Guardi/i.test(speaker) ? 1.15 : 1.0;
            const voice = pickVoice();
            if (voice) { utterance.voice = voice; }
            speechSynthesis.cancel();
            speechSynthesis.speak(utterance);
        } catch (e) { /* sem síntese de voz o jogo segue mudo */ }
    }

    function getSpeakPref() {
        try { return localStorage.getItem('ultima-falar') === '1'; } catch (e) { return false; }
    }

    function setSpeakPref(value) {
        try { localStorage.setItem('ultima-falar', value ? '1' : '0'); } catch (e) { /* sem storage */ }
    }

    function startDictation() {
        if (listening) { return; }
        if (!SR) {
            if (dotnet) {
                dotnet.invokeMethodAsync('OnSpeechError',
                    'Reconhecimento de fala indisponível neste navegador (experimente Chrome ou Edge).');
            }
            return;
        }
        recog = new SR();
        recog.lang = 'pt-BR';
        recog.interimResults = true;
        recog.continuous = false;
        recog.onresult = (e) => {
            let text = '';
            for (const result of e.results) { text += result[0].transcript; }
            if (dotnet) { dotnet.invokeMethodAsync('OnSpeechTranscript', String(text).slice(0, 300)); }
        };
        const finish = () => {
            listening = false;
            if (dotnet) { dotnet.invokeMethodAsync('OnDictation', false); }
        };
        recog.onerror = (e) => {
            finish();
            const why = e && e.error === 'not-allowed' ? 'permissão do microfone negada'
                : 'erro de fala: ' + (e && e.error);
            if (dotnet) { dotnet.invokeMethodAsync('OnSpeechError', why); }
        };
        recog.onend = finish;
        try {
            recog.start();
            listening = true;
            if (dotnet) { dotnet.invokeMethodAsync('OnDictation', true); }
        } catch (e) { finish(); }
    }

    function stopDictation() {
        if (recog && listening) {
            try { recog.stop(); } catch (e) { /* já parado */ }
        }
    }

    window.game.voice = {
        init(dotnetRef) { dotnet = dotnetRef; },
        startMic,
        stopMic,
        speak,
        getSpeakPref,
        setSpeakPref,
        startDictation,
        stopDictation,
        supported() {
            return {
                mic: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
                say: !!window.speechSynthesis,
                hear: !!SR
            };
        }
    };
})();
