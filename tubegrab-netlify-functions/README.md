# TubeGrab - YouTube Downloader

A fully functional YouTube video downloader using Netlify Functions.

## 🚀 Deploy to Netlify

### Option 1: Drag & Drop (Won't work - needs build)
This project requires npm dependencies, so you need to use Git deploy.

### Option 2: Git Deploy (Recommended)
1. Push this folder to a GitHub/GitLab repo
2. Go to [app.netlify.com](https://app.netlify.com)
3. Click "Add new site" → "Import an existing project"
4. Connect your GitHub and select the repo
5. Deploy settings will auto-detect from `netlify.toml`
6. Click "Deploy site"

Netlify will automatically:
- Install dependencies from `package.json`
- Deploy the functions
- Serve the static files

## 📁 Structure

```
├── public/
│   ├── index.html      # Main app
│   └── 404.html        # Error page
├── netlify/
│   └── functions/
│       ├── get-info.js  # Fetches video metadata
│       └── download.js  # Handles download redirect
├── package.json         # Dependencies
├── netlify.toml         # Netlify config
└── README.md
```

## ⚙️ How It Works

1. User pastes YouTube URL
2. `get-info` function fetches video metadata using ytdl-core
3. Available formats are displayed
4. User clicks download
5. `download` function redirects to the direct video URL

## ⚠️ Limitations

- YouTube may rate-limit or block requests
- Some videos may not be available (age-restricted, private, etc.)
- High-quality formats (1080p+) may be video-only (no audio)

## 📝 Note

This is for personal/educational use only. Respect copyright and YouTube's Terms of Service.
