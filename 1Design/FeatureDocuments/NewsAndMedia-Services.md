# News and Media Services Design

**Status:** Draft
**Created:** 2026-01-17
**Priority:** Medium (after Financial API)

## Overview

Personal attention management system for news and video content. Curates information flow based on user interests rather than platform engagement algorithms.

## Core Philosophy

- **User controls attention** - not algorithms optimizing for engagement
- **Quick triage** - skim headlines/thumbnails, decide quickly
- **Learn from feedback** - rejected items inform future filtering
- **Don't miss important things** - breaking news overrides topic filters
- **Source awareness** - track bias/trust levels of sources

---

## Common Concepts

### Item States

| State | Icon | Meaning |
|-------|------|---------|
| New | ● | Not yet seen |
| Seen | ○ | Skimmed but no action taken |
| Hold | ⏸ | Want to review/watch later |
| Done | ✓ | Watched/read, no longer relevant |
| Rejected | ✗ | Not interested, don't show again |

### Interest Scoring

Factors affecting item priority:
- **Topic preferences** - explicit user settings (e.g., "FIFA = low interest")
- **Source trust level** - trusted, neutral, biased-but-useful, blocked
- **Implicit patterns** - learned from Rejected/Done history
- **Breaking indicators** - keywords suggesting important news override filters
- **Age decay** - older items score lower unless in Hold

### Global Item Identification

**Critical:** Every item (news article or video) must have a globally unique ID that:
- Is deterministic (same item always gets same ID)
- Is unique across all sources and types
- Can be used to recognize previously seen/classified material

**ID Format:** `{type}:{source}:{item_id}`

| Type | Source | Item ID | Example |
|------|--------|---------|---------|
| news | source slug | article GUID or URL hash | `news:bbc:a1b2c3d4` |
| video | "youtube" | video ID | `video:youtube:dQw4w9WgXcQ` |

**News Article ID Generation:**
```
if article.guid exists and is valid:
    itemId = sha256(article.guid)[0:12]
else:
    itemId = sha256(article.link)[0:12]

globalId = f"news:{source.slug}:{itemId}"
```

**YouTube Video ID:**
```
globalId = f"video:youtube:{video.id}"
```

YouTube video IDs are already globally unique, so we use them directly.

### Configuration Storage

All configuration via JSON files in `%APPDATA%\LifeStream[-Debug]\Config\`:
- `news-sources.json` - RSS feed configuration
- `youtube-channels.json` - Channel subscriptions
- `topic-preferences.json` - Interest levels by topic
- `blocked-sources.json` - Blocked sources/authors

---

## News Service

### Data Sources (Initial)

| Source | Feed URL | Category | Notes |
|--------|----------|----------|-------|
| ABC News Australia | https://www.abc.net.au/news/feed/... | News | Reputable |
| BBC News | https://feeds.bbci.co.uk/news/rss.xml | News | Reputable |
| SMH | https://www.smh.com.au/rss/... | News | Right-wing skew noted |
| Reuters | https://www.reuters.com/rssFeed/... | News | Wire service, factual |
| Slashdot | https://rss.slashdot.org/Slashdot/slashdotMain | Tech | Tech/nerd news |

### Data Model

```
NewsSource {
    id: string
    name: string
    feedUrl: string
    category: "news" | "tech" | "finance" | "opinion"
    trustLevel: "trusted" | "neutral" | "biased" | "blocked"
    biasDirection?: "left" | "right" | "sensational"
    enabled: bool
    refreshIntervalMinutes: int (default 30)
    lastFetched: datetime
}

NewsArticle {
    id: string (guid from feed)
    sourceId: string
    title: string
    summary: string
    link: string
    author?: string
    published: datetime
    fetchedAt: datetime
    topics: string[]  // extracted keywords
    state: ItemState
    interestScore: float (0-1)
    userNotes?: string
}

TopicPreference {
    topic: string (normalized keyword)
    interestLevel: 1-5 (1=block, 2=low, 3=neutral, 4=high, 5=must-see)
    createdAt: datetime
    source: "explicit" | "learned"
}
```

### Interest Algorithm

```
calculateScore(article):
    score = 0.5  // baseline

    // Source filtering
    if source.trustLevel == "blocked": return 0
    if source.biasDirection in user.avoidBias: score *= 0.5

    // Topic filtering
    for topic in article.topics:
        pref = getTopicPreference(topic)
        if pref.interestLevel == 1: return 0.05  // blocked but visible
        if pref.interestLevel == 2: score *= 0.3
        if pref.interestLevel == 4: score *= 1.5
        if pref.interestLevel == 5: score *= 2.0

    // Breaking news override
    if containsBreakingIndicators(article.title):
        score = max(score, 0.7)

    // Age decay (older = lower priority)
    hoursOld = (now - article.published).hours
    if hoursOld > 24: score *= 0.8
    if hoursOld > 48: score *= 0.5

    // Similar rejection penalty
    if similarHeadlineRejectedRecently(article):
        score *= 0.3

    return clamp(score, 0, 1)
```

### Panel Features

- Headlines list with source icon, age, interest indicator
- Category tabs or filter dropdown
- Quick actions: Mark Read, Hold, Reject, "Lower topic interest"
- Click to expand summary or open in browser
- Bulk actions for older items

---

## Media Service (YouTube)

### Data Strategy

**Primary: RSS Feeds (no API key needed)**
- URL: `https://www.youtube.com/feeds/videos.xml?channel_id=CHANNEL_ID`
- Provides: video ID, title, published date, channel info
- Thumbnail URL derivable: `https://i.ytimg.com/vi/VIDEO_ID/mqdefault.jpg`

**Optional: YouTube Data API v3**
- For: view count, duration, description
- Quota: 10,000 units/day (careful usage)
- Only fetch on-demand or for Hold items

### Media Categories

| Category | Purpose | Examples |
|----------|---------|----------|
| News | Current events, journalism | News channels, documentaries |
| Education | Learning, tutorials | Educational channels, how-to |
| Entertainment | Relaxation, fun | Music, comedy, gaming |

### Data Model

```
YouTubeChannel {
    id: string (channel ID)
    name: string
    category: "news" | "education" | "entertainment"
    priority: 1-5 (higher = check more often)
    enabled: bool
    lastFetched: datetime
}

YouTubeVideo {
    id: string (video ID)
    channelId: string
    title: string
    published: datetime
    thumbnailUrl: string
    duration?: string (from API)
    views?: int (from API)
    state: ItemState
    addedAt: datetime
    watchedAt?: datetime
    userNotes?: string
}
```

### Configuration Example (youtube-channels.json)

```json
{
  "channels": [
    {
      "id": "UCxxxxxxxxxxxxxx",
      "name": "Channel Name",
      "category": "education",
      "priority": 4,
      "enabled": true
    }
  ]
}
```

### Panel Features

- Grid of video thumbnails or list view
- Filter by: channel, category, state, age
- Sort by: newest, priority, channel
- Quick actions: Watch, Hold, Done, Reject
- "New since last session" badge
- Click thumbnail to open in browser or queue

---

## Alert Panel (Top Bar)

### Purpose

Surfaces high-priority items across all services without requiring panel switching.

### Triggers

- N new high-interest news items
- New video from high-priority channel
- Breaking news detected
- Hold queue has aged items needing attention

### Display

- Compact bar at top of dashboard
- Shows count/summary per service
- Click to jump to relevant panel
- Can be dismissed/minimized

---

## Implementation Phases

### Phase 1: Basic Feeds (MVP)
- [ ] News: RSS fetch for ABC, BBC, Slashdot
- [ ] News: Simple headline list with state tracking
- [ ] YouTube: Channel RSS fetch (manual channel config)
- [ ] YouTube: Video list with thumbnails
- [ ] Both: Basic New/Done/Rejected states
- [ ] JSON configuration files

### Phase 2: Organization
- [ ] News: Category grouping
- [ ] News: Topic extraction (keyword-based)
- [ ] YouTube: Category grouping
- [ ] Both: Hold queue
- [ ] Both: Age indicators

### Phase 3: Interest Learning
- [ ] Topic preferences UI
- [ ] Source trust level configuration
- [ ] Interest scoring algorithm
- [ ] "Lower interest" quick action
- [ ] Similar-rejection detection

### Phase 4: Refinement
- [ ] Alert panel
- [ ] Cross-source deduplication
- [ ] Breaking news detection
- [ ] YouTube API integration (optional enrichment)
- [ ] Statistics/insights on consumption patterns

---

## Open Questions

1. Should rejected items be hidden completely or shown dimmed at bottom?
2. How long to retain old articles/videos? (suggest 7 days for news, 30 for videos)
3. Should there be a "Watch Later" export to actual YouTube queue?
4. Keyboard shortcuts for quick triage?

---

## Related Documents

- Architecture.md - Overall system design
- Financial-Service.md - Similar service pattern
