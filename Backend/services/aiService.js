const axios = require('axios');

const AI_SERVICE_URL = process.env.AI_SERVICE_URL || 'http://localhost:8000';

class AIService {
  static async startDetection(rtspUrl, location, userId) {
    try {
      const response = await axios.post(`${AI_SERVICE_URL}/start-detection`, {
        rtsp_url: rtspUrl,
        location: location,
        user_id: userId
      }, {
        timeout: 10000
      });

      console.log('AI Detection started:', response.data);
      return { success: true, data: response.data };
    } catch (error) {
      console.error('Failed to start AI detection:', error.message);
      return { success: false, error: error.message };
    }
  }

  static async stopDetection() {
    try {
      const response = await axios.post(`${AI_SERVICE_URL}/stop-detection`, {
        timeout: 5000
      });

      console.log('AI Detection stopped:', response.data);
      return { success: true, data: response.data };
    } catch (error) {
      console.error('Failed to stop AI detection:', error.message);
      return { success: false, error: error.message };
    }
  }

  static async checkHealth() {
    try {
      const response = await axios.get(`${AI_SERVICE_URL}/health`, {
        timeout: 5000
      });

      return { success: true, data: response.data };
    } catch (error) {
      return { success: false, error: error.message };
    }
  }
}

module.exports = AIService;