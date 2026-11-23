// Next.js asks for sharp only to optimize images on a server; the page is a static export, so this is never reached.
throw new Error('Trovolve leaves sharp out: its page is a static export, which optimizes no images.');
