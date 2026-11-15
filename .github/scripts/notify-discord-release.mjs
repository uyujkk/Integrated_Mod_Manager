import { readFile } from 'node:fs/promises';

const guildId = '1565241383378358394';
const channelId = '1565529795352338462';
const releaseUrl = /^https:\/\/github\.com\/uyujkk\/Integrated_Mod_Manager\/releases\/tag\/[\w.+%-]+$/;
const webhookUrl = /^https:\/\/discord\.com\/api\/(?:v\d+\/)?webhooks\/\d{17,22}\/[\w.-]+$/;
const issuesHeading = /^#{2,3}[ \t]+(?:已知问题|Known issues)[^\n]*$/im;

function excerpt(text, limit) {
  if (text.length <= limit) return text;
  const lastLine = text.lastIndexOf('\n', limit - 4);
  const cut = lastLine >= limit / 2 ? lastLine : limit - 4;
  return text.slice(0, cut).trimEnd() + '\n…';
}

function formatSection(text) {
  const limit = 2800;
  if (text.length <= limit) return text;
  const issues = issuesHeading.exec(text);
  if (!issues || issues.index === 0) return excerpt(text, limit);
  const knownIssues = excerpt(text.slice(issues.index).trim(), 700);
  const updates = excerpt(text.slice(0, issues.index).trim(), limit - knownIssues.length - 2);
  return updates + '\n\n' + knownIssues;
}

function releaseEmbeds(body) {
  const text = body.replace(/\r\n?/g, '\n').replace(/<!--[\s\S]*?-->/g, '').trim();
  const chinese = /^#{2,3}[ \t]+(?:中文|更新内容)[ \t]*$/m.exec(text);
  const english = /^#{2,3}[ \t]+(?:English|What['’]s new)[ \t]*$/im.exec(text);
  if (!chinese || !english || english.index <= chinese.index) {
    return [{ title: 'Release notes', description: excerpt(text || 'See the GitHub release for downloads.', 3800) }];
  }
  const zhStart = /中文[ \t]*$/.test(chinese[0]) ? chinese.index + chinese[0].length : chinese.index;
  const enStart = /English[ \t]*$/i.test(english[0]) ? english.index + english[0].length : english.index;
  const zh = formatSection(text.slice(zhStart, english.index).trim());
  const en = formatSection(text.slice(enStart).trim());
  if (!zh || !en) {
    return [{ title: 'Release notes', description: excerpt(text, 3800) }];
  }
  return [
    { title: '中文', color: 15105570, description: zh },
    { title: 'English', color: 5793266, description: en },
  ];
}

export function buildDiscordPayload(release) {
  if (!releaseUrl.test(release.html_url) || !/^v\d+\.\d+\.\d+$/.test(release.tag_name)) {
    throw new Error('Expected an IMM stable version and its GitHub release URL.');
  }
  const link = `[完整说明及下载 · Full notes and downloads](${release.html_url})`;
  return {
    username: 'IMM Releases',
    content: `**Integrated Mod Manager ${release.tag_name}**\n${link}`,
    allowed_mentions: { parse: [] },
    embeds: releaseEmbeds(release.body ?? ''),
  };
}

async function requestWebhook(url, options) {
  let response;
  try {
    response = await fetch(url, { ...options, redirect: 'error', signal: AbortSignal.timeout(20000) });
  } catch {
    throw new Error('Discord webhook request failed or timed out.');
  }
  if (!response.ok) throw new Error(`Discord webhook returned HTTP ${response.status}.`);
  return response.json();
}

export async function sendRelease() {
  const event = JSON.parse(await readFile(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const release = event.release;
  if (event.action !== 'published' || !release || release.draft || release.prerelease) {
    return;
  }
  const url = process.env.DISCORD_RELEASE_WEBHOOK_URL;
  if (!url || !webhookUrl.test(url)) throw new Error('DISCORD_RELEASE_WEBHOOK_URL is missing or invalid.');
  const payload = buildDiscordPayload(release);
  const webhook = await requestWebhook(url);
  if (webhook.guild_id !== guildId || webhook.channel_id !== channelId) {
    throw new Error('The webhook does not point to IMM releases-版本.');
  }
  const message = await requestWebhook(`${url}?wait=true`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  if (!/^\d{17,22}$/.test(message.id) || message.channel_id !== channelId) {
    throw new Error('Discord did not confirm the release message.');
  }
  console.log(`Posted ${release.tag_name} to https://discord.com/channels/${guildId}/${channelId}/${message.id}`);
}
