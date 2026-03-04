/**
 * Routes Index
 * Export all route modules
 */

module.exports = {
  authRoutes: require('./auth'),
  dashboardRoutes: require('./dashboard'),
  notificationsRoutes: require('./notifications')
};
