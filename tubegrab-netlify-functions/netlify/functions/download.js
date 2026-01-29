const ytdl = require('@distube/ytdl-core');

exports.handler = async (event) => {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type'
  };

  if (event.httpMethod === 'OPTIONS') {
    return { statusCode: 200, headers, body: '' };
  }

  try {
    const { url, itag } = event.queryStringParameters || {};
    
    if (!url || !ytdl.validateURL(url)) {
      return {
        statusCode: 400,
        headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify({ error: 'Invalid YouTube URL' })
      };
    }

    const info = await ytdl.getInfo(url);
    const title = info.videoDetails.title.replace(/[^\w\s-]/g, '').substring(0, 50);
    
    // Find the requested format or default to best
    let format;
    if (itag) {
      format = info.formats.find(f => f.itag === parseInt(itag));
    }
    if (!format) {
      format = ytdl.chooseFormat(info.formats, { quality: 'highest', filter: 'audioandvideo' });
    }

    const extension = format.container || 'mp4';

    return {
      statusCode: 302,
      headers: {
        ...headers,
        'Location': format.url,
        'Content-Disposition': `attachment; filename="${title}.${extension}"`
      },
      body: ''
    };
  } catch (error) {
    console.error('Error:', error);
    return {
      statusCode: 500,
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({ error: 'Download failed: ' + error.message })
    };
  }
};
