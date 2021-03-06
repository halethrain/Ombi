﻿#region Copyright
// /************************************************************************
//    Copyright (c) 2017 Jamie Rees
//    File: EmbyAvaliabilityCheker.cs
//    Created By: Jamie Rees
//   
//    Permission is hereby granted, free of charge, to any person obtaining
//    a copy of this software and associated documentation files (the
//    "Software"), to deal in the Software without restriction, including
//    without limitation the rights to use, copy, modify, merge, publish,
//    distribute, sublicense, and/or sell copies of the Software, and to
//    permit persons to whom the Software is furnished to do so, subject to
//    the following conditions:
//   
//    The above copyright notice and this permission notice shall be
//    included in all copies or substantial portions of the Software.
//   
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
//    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//    NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
//    LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
//    OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
//    WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//  ************************************************************************/
#endregion

using System;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Ombi.Core.Notifications;
using Ombi.Helpers;
using Ombi.Notifications.Models;
using Ombi.Store.Entities;
using Ombi.Store.Repository;
using Ombi.Store.Repository.Requests;

namespace Ombi.Schedule.Jobs.Emby
{
    public class EmbyAvaliabilityChecker : IEmbyAvaliabilityChecker
    {
        public EmbyAvaliabilityChecker(IEmbyContentRepository repo, ITvRequestRepository t, IMovieRequestRepository m,
            INotificationService n)
        {
            _repo = repo;
            _tvRepo = t;
            _movieRepo = m;
            _notificationService = n;
        }

        private readonly ITvRequestRepository _tvRepo;
        private readonly IMovieRequestRepository _movieRepo;
        private readonly IEmbyContentRepository _repo;
        private readonly INotificationService _notificationService;

        public async Task Start()
        {
            await ProcessMovies();
            await ProcessTv();
        }

        private async Task ProcessMovies()
        {
            var movies = _movieRepo.Get().Where(x => !x.Available);

            foreach (var movie in movies)
            {
                var embyContent = await _repo.Get(movie.ImdbId);
                if (embyContent == null)
                {
                    // We don't have this yet
                    continue;
                }

                movie.Available = true;
                if (movie.Available)
                {
                    BackgroundJob.Enqueue(() => _notificationService.Publish(new NotificationOptions
                    {
                        DateTime = DateTime.Now,
                        NotificationType = NotificationType.RequestAvailable,
                        RequestId = movie.Id,
                        RequestType = RequestType.Movie
                    }));
                }
            }
            await _movieRepo.Save();
        }



        /// <summary>
        /// TODO This is EXCATLY the same as the PlexAvailabilityChecker. Refactor Please future Jamie
        /// </summary>
        /// <returns></returns>
        private async Task ProcessTv()
        {
            var tv = _tvRepo.GetChild().Where(x => !x.Available);
            var embyEpisodes = _repo.GetAllEpisodes().Include(x => x.Series);

            foreach (var child in tv)
            {
                var tvDbId = child.ParentRequest.TvDbId;
                var seriesEpisodes = embyEpisodes.Where(x => x.Series.ProviderId == tvDbId.ToString());
                foreach (var season in child.SeasonRequests)
                {
                    foreach (var episode in season.Episodes)
                    {
                        var foundEp = await seriesEpisodes.FirstOrDefaultAsync(
                            x => x.EpisodeNumber == episode.EpisodeNumber &&
                                 x.SeasonNumber == episode.Season.SeasonNumber);

                        if (foundEp != null)
                        {
                            episode.Available = true;
                        }
                    }
                }

                // Check to see if all of the episodes in all seasons are available for this request
                var allAvailable = child.SeasonRequests.All(x => x.Episodes.All(c => c.Available));
                if (allAvailable)
                {
                    // We have fulfulled this request!
                    child.Available = true;
                    BackgroundJob.Enqueue(() => _notificationService.Publish(new NotificationOptions
                    {
                        DateTime = DateTime.Now,
                        NotificationType = NotificationType.RequestAvailable,
                        RequestId = child.ParentRequestId,
                        RequestType = RequestType.TvShow
                    }));
                }
            }

            await _tvRepo.Save();
        }
    }
}