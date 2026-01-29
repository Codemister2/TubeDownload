const ytdl = require('@distube/ytdl-core');

exports.handler = async (event) => {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type',
    'Content-Type': 'application/json'
  };

  if (event.httpMethod === 'OPTIONS') {
    return { statusCode: 200, headers, body: '' };
  }

  try {
    const { url } = JSON.parse(event.body || '{}');
    
    if (!url || !ytdl.validateURL(url)) {
      return {
        statusCode: 400,
        headers,
        body: JSON.stringify({ error: 'Invalid YouTube URL' })
      };
    }

    const info = await ytdl.getInfo(url);
    
    // Get available formats
    const formats = info.formats
      .filter(f => f.hasVideo && f.hasAudio)
      .map(f => ({
        itag: f.itag,
        quality: f.qualityLabel,
        container: f.container,
        size: f.contentLength ? Math.round(f.contentLength / 1024 / 1024) + ' MB' : 'Unknown'
      }))
      .filter((v, i, a) => a.findIndex(t => t.quality === v.quality) === i)
      .slice(0, 6);

    // Also get video-only formats for higher quality
    const videoFormats = info.formats
      .filter(f => f.hasVideo && !f.hasAudio && f.qualityLabel)
      .map(f => ({
        itag: f.itag,
        quality: f.qualityLabel,
        container: f.container,
        size: f.contentLength ? Math.round(f.contentLength / 1024 / 1024) + ' MB' : 'Unknown',
        videoOnly: true
      }))
      .filter((v, i, a) => a.findIndex(t => t.quality === v.quality) === i);

    // Get audio formats
    const audioFormats = info.formats
      .filter(f => f.hasAudio && !f.hasVideo)
      .map(f => ({
        itag: f.itag,
        quality: f.audioBitrate + 'kbps',
        container: f.container,
        size: f.contentLength ? Math.round(f.contentLength / 1024 / 1024) + ' MB' : 'Unknown',
        audioOnly: true
      }))
      .slice(0, 3);

    return {
      statusCode: 200,
      headers,
      body: JSON.stringify({
        title: info.videoDetails.title,
        thumbnail: info.videoDetails.thumbnails.pop()?.url,
        duration: info.videoDetails.lengthSeconds,
        author: info.videoDetails.author.name,
        views: info.videoDetails.viewCount,
        formats: [...formats, ...videoFormats, ...audioFormats]
      })
    };
  } catch (error) {
    console.error('Error:', error);
    return {
      statusCode: 500,
      headers,
      body: JSON.stringify({ error: 'Failed to fetch video info: ' + error.message })
    };
  }
};
