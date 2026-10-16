/**
 * Telegram Bot API 反代转发器 — Cloudflare Worker 版
 * ============================================================
 * 作用：让国内 PC 不连梯子也能调用 Telegram Bot API。
 * 原理：PC 请求 https://<你的worker>.workers.dev/<SECRET>/bot<token>/sendMessage，
 *      Worker 把 <SECRET>/ 之后的部分原样转发到 https://api.telegram.org/。
 *
 * 部署步骤（2 分钟）：
 *  1. 注册/登录 cloudflare.com → 左侧 Workers → Create Worker
 *  2. 把本文件内容粘贴进去，先把下面 SECRET 改成一个随机长字符串
 *     （例如用密码生成器生成 32 位，只给自己和托盘软件用）
 *  3. Deploy。记下 Worker 地址，如 https://yuno-tg.xxx.workers.dev
 *  4. 在托盘软件配置里填「Telegram API 基地址」：
 *        https://yuno-tg.xxx.workers.dev/<SECRET>/
 *     （注意末尾带斜杠；不填则默认直连 https://api.telegram.org/）
 *
 * 可靠性说明：
 *  - workers.dev 域名在国内一般可直接访问，不需要梯子；
 *    如需更稳，可在 Cloudflare 里给 Worker 绑自己的域名。
 *  - 免费额度每天 10 万次请求，开关机通知这点量随便用。
 *  - SECRET 路径是防别人盗用你的 Worker 额度用的，不要泄露。
 */

const SECRET = "在这里填一个随机长字符串-只给自己用"; // ←←← 改这里

export default {
  async fetch(request) {
    const url = new URL(request.url);

    // 鉴权：路径必须以 /<SECRET>/ 开头，否则 403
    const prefix = `/${SECRET}/`;
    if (!url.pathname.startsWith(prefix)) {
      return new Response("Forbidden", { status: 403 });
    }

    // 目标：https://api.telegram.org/bot<token>/sendMessage?...（原样转发）
    const target =
      "https://api.telegram.org/" +
      url.pathname.slice(prefix.length) +
      url.search;

    const headers = new Headers(request.headers);
    headers.delete("host");
    headers.delete("cf-connecting-ip");

    return fetch(target, {
      method: request.method,
      headers,
      body: ["GET", "HEAD"].includes(request.method)
        ? undefined
        : await request.arrayBuffer(),
      redirect: "manual",
    });
  },
};
